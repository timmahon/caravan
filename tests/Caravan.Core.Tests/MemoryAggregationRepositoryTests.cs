using Caravan.Aggregation;
using Caravan.Spi;

namespace Caravan.Core.Tests;

public class MemoryAggregationRepositoryTests : ContextTestBase
{
    private Exchange NewExchange(object? body)
    {
        var exchange = new Exchange(Context);
        exchange.Message.Body = body;
        return exchange;
    }

    [Fact]
    public async Task Stores_and_returns_copies()
    {
        var repository = new MemoryAggregationRepository();
        var exchange = NewExchange("a");

        await repository.AddAsync(Context, "k", exchange);
        exchange.Message.Body = "changed";
        var first = await repository.GetAsync(Context, "k");
        first!.Message.Body = "changed too";
        var second = await repository.GetAsync(Context, "k");

        Assert.Equal("a", second!.Message.Body);
        Assert.Equal(exchange.ExchangeId, second.ExchangeId);
        Assert.NotSame(first, second);
        Assert.Equal(["k"], await repository.GetKeysAsync());
        Assert.Null(await repository.GetAsync(Context, "other"));
    }

    [Fact]
    public async Task Remove_and_stop_forget_groups()
    {
        var repository = new MemoryAggregationRepository();
        await repository.StartAsync();
        await repository.AddAsync(Context, "a", NewExchange(1));
        await repository.AddAsync(Context, "b", NewExchange(2));

        await repository.RemoveAsync(Context, "a", (await repository.GetAsync(Context, "a"))!);
        await repository.ConfirmAsync(Context, "whatever");
        Assert.Equal(["b"], await repository.GetKeysAsync());

        await repository.StopAsync();
        Assert.Equal(0, repository.Count);
    }

    [Fact]
    public async Task Optimistic_locking_detects_concurrent_updates()
    {
        var repository = new MemoryAggregationRepository(optimisticLocking: true);
        await repository.AddAsync(Context, "k", null, NewExchange("v1"));
        await Assert.ThrowsAsync<OptimisticLockingException>(() => repository.AddAsync(Context, "k", null, NewExchange("dup")).AsTask());

        var read1 = (await repository.GetAsync(Context, "k"))!;
        var read2 = (await repository.GetAsync(Context, "k"))!;
        Assert.Equal(1L, read1.GetProperty(ExchangePropertyKeys.AggregationRepositoryVersion));

        await repository.AddAsync(Context, "k", read1, NewExchange("v2"));
        await Assert.ThrowsAsync<OptimisticLockingException>(() => repository.AddAsync(Context, "k", read2, NewExchange("lost")).AsTask());
        await Assert.ThrowsAsync<OptimisticLockingException>(() => repository.RemoveAsync(Context, "k", read2).AsTask());

        var current = (await repository.GetAsync(Context, "k"))!;
        Assert.Equal("v2", current.Message.Body);
        Assert.Equal(2L, current.GetProperty(ExchangePropertyKeys.AggregationRepositoryVersion));
        await repository.RemoveAsync(Context, "k", current);
        Assert.Empty(await repository.GetKeysAsync());
        await Assert.ThrowsAsync<OptimisticLockingException>(() => repository.AddAsync(Context, "k", current, NewExchange("gone")).AsTask());
    }

    [Fact]
    public async Task Optimistic_locking_does_not_mistake_a_new_group_for_a_removed_one()
    {
        var repository = new MemoryAggregationRepository(optimisticLocking: true);
        await repository.AddAsync(Context, "k", null, NewExchange("first group"));
        var stale = (await repository.GetAsync(Context, "k"))!;
        await repository.RemoveAsync(Context, "k", stale);
        await repository.AddAsync(Context, "k", null, NewExchange("second group"));

        await Assert.ThrowsAsync<OptimisticLockingException>(() => repository.AddAsync(Context, "k", stale, NewExchange("lost")).AsTask());
        await Assert.ThrowsAsync<OptimisticLockingException>(() => repository.RemoveAsync(Context, "k", stale).AsTask());
        Assert.Equal("second group", (await repository.GetAsync(Context, "k"))!.Message.Body);
    }

    [Fact]
    public async Task Optimistic_locking_copies_the_grouped_list_so_a_failed_attempt_leaves_the_group_unchanged()
    {
        var repository = new MemoryAggregationRepository(optimisticLocking: true);
        var strategy = AggregationStrategies.GroupedBody();
        await repository.AddAsync(Context, "k", null, strategy.Aggregate(null, NewExchange("a"))!);

        var read1 = (await repository.GetAsync(Context, "k"))!;
        var read2 = (await repository.GetAsync(Context, "k"))!;
        await repository.AddAsync(Context, "k", read1, strategy.Aggregate(read1, NewExchange("b"))!);
        var lost = strategy.Aggregate(read2, NewExchange("c"))!;
        await Assert.ThrowsAsync<OptimisticLockingException>(() => repository.AddAsync(Context, "k", read2, lost).AsTask());

        var current = (await repository.GetAsync(Context, "k"))!;
        Assert.Equal(["a", "b"], (List<object?>)current.GetProperty(ExchangePropertyKeys.GroupedExchange)!);
    }

    [Fact]
    public async Task Without_optimistic_locking_the_conditional_add_always_stores()
    {
        var repository = new MemoryAggregationRepository();
        await repository.AddAsync(Context, "k", null, NewExchange("v1"));
        await repository.AddAsync(Context, "k", null, NewExchange("v2"));

        Assert.Equal("v2", (await repository.GetAsync(Context, "k"))!.Message.Body);
        Assert.Null((await repository.GetAsync(Context, "k"))!.GetProperty(ExchangePropertyKeys.AggregationRepositoryVersion));
    }

    [Fact]
    public void Retry_policy_backs_off_exponentially_up_to_the_maximum()
    {
        var policy = new OptimisticLockRetryPolicy { RetryDelay = TimeSpan.FromMilliseconds(100), MaximumRetryDelay = TimeSpan.FromMilliseconds(500), MaximumRetries = 3 };

        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(200), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(400), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.GetDelay(10));
        Assert.True(policy.ShouldRetry(2));
        Assert.False(policy.ShouldRetry(3));
        Assert.True(new OptimisticLockRetryPolicy().ShouldRetry(1000));

        policy.ExponentialBackOff = false;
        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.GetDelay(5));
        policy.RandomBackOff = true;
        Assert.InRange(policy.GetDelay(5), TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }
}
