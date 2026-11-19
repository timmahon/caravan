using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Idempotent;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Core.Tests;

/// <summary>idempotentConsumer semantics (mirrors Camel's IdempotentConsumer* tests) and the memory repository.</summary>
public class IdempotentConsumerTests : ContextTestBase
{
    private readonly MemoryIdempotentRepository _repository = new();

    private Task Send(string id, object? body = null) => Template.SendBodyAndHeaderAsync("direct:start", body ?? id, "id", id);

    [Fact]
    public async Task Duplicates_skip_the_block_and_are_marked()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .IdempotentConsumer(Expr.Header("id"), _repository, i => i.To("mock:result"))
            .To("mock:after"));
        Mock("result").ExpectedBodiesReceived("one", "two", "three");
        Mock("after").ExpectedMessageCount(5);

        await Send("1", "one");
        await Send("2", "two");
        await Send("1", "one again");
        await Send("3", "three");
        await Send("2", "two again");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(
            [null, null, true, null, true],
            Mock("after").ReceivedExchanges.Select(e => e.GetProperty(ExchangePropertyKeys.DuplicateMessage)));
        Assert.Equal(3, _repository.Count);
    }

    [Fact]
    public async Task Without_skip_duplicate_the_block_runs_with_the_duplicate_property()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .IdempotentConsumer(
                e => e.Message.GetHeader("id"),
                _repository,
                i => i.Filter(Expr.Property(ExchangePropertyKeys.DuplicateMessage).IsEqualTo(true), f => f.To("mock:duplicate").Stop()).To("mock:result"),
                skipDuplicate: false));
        Mock("result").ExpectedBodiesReceived("a", "b");
        Mock("duplicate").ExpectedBodiesReceived("a2");

        await Send("1", "a");
        await Send("1", "a2");
        await Send("2", "b");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Null_message_id_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").IdempotentConsumer(Expr.Header("id"), _repository, i => i.To("mock:result")));
        ExpectNone("result");

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "no id"));

        Assert.IsType<NoMessageIdException>(ex.InnerException);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Failed_exchange_removes_its_id_so_it_can_be_retried()
    {
        var fail = true;
        await StartWithRoutes(b => b.From("direct:start").IdempotentConsumer(Expr.Header("id"), _repository, i => i
            .Process(_ =>
            {
                if (fail)
                {
                    throw new InvalidOperationException("first attempt fails");
                }
            })
            .To("mock:result")));
        Mock("result").ExpectedMessageCount(1);

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Send("1"));
        Assert.False(await _repository.ContainsAsync("1"));
        fail = false;
        await Send("1");
        await Send("1");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Without_remove_on_failure_a_failed_id_stays()
    {
        await StartWithRoutes(b => b.From("direct:start").IdempotentConsumer(
            Expr.Header("id"),
            _repository,
            i => i.To("mock:result").ThrowException(new InvalidOperationException("boom")),
            removeOnFailure: false));
        Mock("result").ExpectedMessageCount(1);

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Send("1"));
        await Send("1");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.True(await _repository.ContainsAsync("1"));
    }

    [Fact]
    public async Task Non_eager_adds_the_id_only_when_the_exchange_completed()
    {
        bool? containedDuringProcessing = null;
        await StartWithRoutes(b => b.From("direct:start").IdempotentConsumer(
            Expr.Header("id"),
            _repository,
            i => i.Process(e => containedDuringProcessing = _repository.ContainsAsync("1").AsTask().Result).To("mock:result"),
            eager: false));
        Mock("result").ExpectedMessageCount(1);

        await Send("1");
        await Send("1");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.False(containedDuringProcessing);
        Assert.True(await _repository.ContainsAsync("1"));
    }

    [Fact]
    public async Task Completion_eager_confirms_before_later_steps_fail()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:lazy").IdempotentConsumer(Expr.Header("id"), _repository, i => i.To("mock:lazy")).ThrowException(new InvalidOperationException("after"));
            b.From("direct:eager").IdempotentConsumer(Expr.Header("id"), _repository, i => i.To("mock:eager"), completionEager: true)
                .ThrowException(new InvalidOperationException("after"));
        });

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:lazy", "x", "id", "lazy"));
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:eager", "x", "id", "eager"));

        // Completed with the exchange: the later failure removed the id. Completed eagerly: the block succeeded, so the id stays.
        Assert.False(await _repository.ContainsAsync("lazy"));
        Assert.True(await _repository.ContainsAsync("eager"));
    }

    [Fact]
    public async Task Completion_eager_removes_the_id_when_the_block_fails()
    {
        await StartWithRoutes(b => b.From("direct:start").IdempotentConsumer(
            Expr.Header("id"),
            _repository,
            i => i.ThrowException(new InvalidOperationException("boom")),
            completionEager: true));

        await Assert.ThrowsAsync<CaravanExecutionException>(() => Send("1"));

        Assert.False(await _repository.ContainsAsync("1"));
    }

    [Fact]
    public async Task Failure_handled_by_a_dead_letter_channel_counts_as_success()
    {
        await StartWithRoutes(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead"));
            b.From("direct:start").IdempotentConsumer(Expr.Header("id"), _repository, i => i.ThrowException(new InvalidOperationException("boom")));
        });
        Mock("dead").ExpectedMessageCount(1);

        await Send("1");
        await Send("1");

        await Mock("dead").AssertIsSatisfiedAsync();
        Assert.True(await _repository.ContainsAsync("1"));
    }

    [Fact]
    public async Task Eager_lets_exactly_one_of_concurrent_duplicates_through()
    {
        var probe = new ConcurrencyProbe();
        await StartWithRoutes(b => b.From("direct:start")
            .IdempotentConsumer(Expr.Header("id"), _repository, i => i.Process(_ => probe.EnterAsync()).To("mock:result")));
        Mock("result").ExpectedMessageCount(5);

        var sends = Enumerable.Range(0, 40).Select(n => Task.Run(() => Send((n % 5).ToString(System.Globalization.CultureInfo.InvariantCulture)))).ToArray();
        await probe.WaitForStartedAsync(5);
        probe.Release(5);
        await Task.WhenAll(sends).Bounded();

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(5, probe.StartedCount);
        Assert.Equal(5, Mock("result").ReceivedExchanges.Select(e => e.Message.GetHeader("id")).Distinct().Count());
    }

    [Fact]
    public async Task Repository_that_is_a_service_is_started_and_stopped_with_the_route()
    {
        var repository = new ServiceRepository();
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").IdempotentConsumer(Expr.Header("id"), repository, i => i.To("mock:result")));

        Assert.True(repository.IsStarted);
        await Context.StopRouteAsync("r");

        Assert.Equal(Spi.ServiceStatus.Stopped, repository.Status);
    }

    [Fact]
    public async Task Memory_repository_evicts_the_least_recently_added_key()
    {
        var repository = new MemoryIdempotentRepository(maxCacheSize: 2);

        Assert.True(await repository.AddAsync("a"));
        Assert.False(await repository.AddAsync("a"));
        Assert.True(await repository.AddAsync("b"));
        Assert.True(await repository.AddAsync("c"));

        Assert.False(await repository.ContainsAsync("a"));
        Assert.True(await repository.ContainsAsync("b"));
        Assert.True(await repository.ContainsAsync("c"));
        Assert.Equal(2, repository.Count);
        Assert.True(await repository.RemoveAsync("b"));
        Assert.False(await repository.RemoveAsync("b"));
        Assert.True(await repository.ConfirmAsync("c"));
        await repository.ClearAsync();
        Assert.Equal(0, repository.Count);
        Assert.Equal(1000, new MemoryIdempotentRepository().MaxCacheSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryIdempotentRepository(0));
    }

    [Fact]
    public async Task Memory_repository_add_is_atomic_under_contention()
    {
        var repository = new MemoryIdempotentRepository();
        var added = 0;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 200; i++)
            {
                if (await repository.AddAsync(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    Interlocked.Increment(ref added);
                }
            }
        }))).Bounded();

        Assert.Equal(200, added);
    }

    private sealed class ServiceRepository : ServiceSupport, IIdempotentRepository
    {
        private readonly MemoryIdempotentRepository _inner = new();

        public ValueTask<bool> AddAsync(string key, CancellationToken cancellationToken = default) => _inner.AddAsync(key, cancellationToken);

        public ValueTask<bool> ContainsAsync(string key, CancellationToken cancellationToken = default) => _inner.ContainsAsync(key, cancellationToken);

        public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => _inner.RemoveAsync(key, cancellationToken);

        public ValueTask<bool> ConfirmAsync(string key, CancellationToken cancellationToken = default) => _inner.ConfirmAsync(key, cancellationToken);

        public ValueTask ClearAsync(CancellationToken cancellationToken = default) => _inner.ClearAsync(cancellationToken);
    }
}
