using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Model;
using Caravan.Saga;
using Caravan.Spi;

namespace Caravan.Core.Tests;

/// <summary>saga semantics with the in-memory saga service (mirrors Camel's SagaTest, SagaPropagationTest, SagaTimeoutTest, SagaFailuresTest).</summary>
public class SagaTests : ContextTestBase
{
    private const string Lra = CaravanHeaders.SagaLongRunningAction;

    private static List<object?> Headers(MockEndpoint mock, string name) => mock.ReceivedExchanges.Select(e => e.Message.GetHeader(name)).ToList();

    private static string? SagaId(Exchange exchange) => exchange.Message.GetHeader(Lra)?.ToString();

    private InMemorySagaService UseService(int maxRetryAttempts = 2, TimeSpan? retryDelay = null)
    {
        var service = new InMemorySagaService(Context) { MaxRetryAttempts = maxRetryAttempts, RetryDelay = retryDelay ?? TimeSpan.Zero };
        Context.SetSagaService(service);
        return service;
    }

    private static SagaBuilder Step(SagaBuilder s, string name) =>
        s.Compensation("mock:compensate").Completion("mock:complete").Option("step", _ => name);

    [Fact]
    public async Task Required_starts_a_saga_and_completes_it_when_the_block_succeeds()
    {
        string? inside = null;
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(s => Step(s, "a"), body => body.Process(e => inside = SagaId(e)).To("mock:body"))
            .To("mock:after"));
        Mock("complete").ExpectedMessageCount(1);
        Mock("body").ExpectedMessageCount(1);
        ExpectNone("compensate");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "order");

        Assert.Null(exchange.Exception);
        Assert.NotNull(inside);
        Assert.Null(SagaId(exchange));
        Assert.Null(Mock("after").ReceivedExchanges.Single().Message.GetHeader(Lra));
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        var completion = Mock("complete").ReceivedExchanges.Single();
        Assert.Equal(inside, SagaId(completion));
        Assert.Equal("a", completion.Message.GetHeader("step"));
        Assert.Null(completion.Message.Body);
        Assert.Empty(((InMemorySagaService)Context.GetSagaService()).Sagas);
    }

    [Fact]
    public async Task Failure_compensates_the_saga_and_the_exception_propagates()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(s => Step(s, "a"), body => body.ThrowException(new InvalidOperationException("payment refused")))
            .To("mock:after"));
        Mock("compensate").ExpectedMessageCount(1);
        ExpectNone("complete");
        ExpectNone("after");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "order");

        Assert.IsType<InvalidOperationException>(exchange.Exception);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Steps_joining_a_saga_are_compensated_in_reverse_order_with_their_options_as_headers()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .Saga(
                    s => Step(s, "order"),
                    body => body.To("direct:credit").To("direct:ship").ThrowException(new InvalidOperationException("late failure")));
            b.From("direct:credit").Saga(s => Step(s, "credit").Option("amount", Expr.Header("amount")), body => body.To("mock:credit"));
            b.From("direct:ship").Saga(s => Step(s, "ship"), body => body.To("mock:ship"));
        });
        Mock("compensate").ExpectedMessageCount(3);
        ExpectNone("complete");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.SetHeader("amount", 42));

        Assert.IsType<InvalidOperationException>(exchange.Exception);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(["ship", "credit", "order"], Headers(Mock("compensate"), "step"));
        Assert.Equal([null, 42, null], Headers(Mock("compensate"), "amount"));
        Assert.Single(Headers(Mock("compensate"), Lra).Distinct());
        Assert.Equal(SagaId(Mock("credit").ReceivedExchanges.Single()), SagaId(Mock("ship").ReceivedExchanges.Single()));
    }

    [Fact]
    public async Task All_completion_endpoints_are_called_in_reverse_order_when_the_saga_completes()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Saga(s => Step(s, "order"), body => body.To("direct:credit"));
            b.From("direct:credit").Saga(s => Step(s, "credit"), body => body.To("mock:credit"));
        });
        Mock("complete").ExpectedMessageCount(2);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("complete").AssertIsSatisfiedAsync();
        Assert.Equal(["credit", "order"], Headers(Mock("complete"), "step"));
    }

    [Fact]
    public async Task Option_values_are_evaluated_when_the_step_begins()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(s => s.Completion("mock:complete").Option("id", Expr.Header("orderId")), body => body.SetHeader("orderId", _ => "changed")));
        Mock("complete").ExpectedHeaderReceived("id", "o-1");

        await Template.SendBodyAndHeaderAsync("direct:start", null, "orderId", "o-1");

        await Mock("complete").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Requires_new_starts_a_nested_saga_and_restores_the_outer_one()
    {
        string? outer = null;
        string? inner = null;
        string? after = null;
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(
                s => s.Compensation("mock:outerCompensate").Completion("mock:outerComplete"),
                body => body
                    .Process(e => outer = SagaId(e))
                    .Saga(
                        s => s.Propagation(SagaPropagation.RequiresNew).Compensation("mock:innerCompensate").Completion("mock:innerComplete"),
                        nested => nested.Process(e => inner = SagaId(e)))
                    .Process(e => after = SagaId(e))
                    .ThrowException(new InvalidOperationException("outer fails after the inner saga completed"))));
        Mock("innerComplete").ExpectedMessageCount(1);
        Mock("outerCompensate").ExpectedMessageCount(1);
        ExpectNone("innerCompensate");
        ExpectNone("outerComplete");

        await Template.SendAsync("direct:start", _ => { });

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.NotNull(outer);
        Assert.NotNull(inner);
        Assert.NotEqual(outer, inner);
        Assert.Equal(outer, after);
    }

    [Fact]
    public async Task Failing_nested_saga_compensates_both_sagas()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(
                s => s.Compensation("mock:outerCompensate"),
                body => body.Saga(
                    s => s.Propagation(SagaPropagation.RequiresNew).Compensation("mock:innerCompensate"),
                    nested => nested.ThrowException(new InvalidOperationException("inner")))));
        Mock("innerCompensate").ExpectedMessageCount(1);
        Mock("outerCompensate").ExpectedMessageCount(1);

        var exchange = await Template.SendAsync("direct:start", _ => { });

        Assert.IsType<InvalidOperationException>(exchange.Exception);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.NotEqual(SagaId(Mock("innerCompensate").ReceivedExchanges.Single()), SagaId(Mock("outerCompensate").ReceivedExchanges.Single()));
    }

    [Fact]
    public async Task Mandatory_fails_without_a_saga_and_joins_an_existing_one()
    {
        string? outer = null;
        string? joined = null;
        await StartWithRoutes(b =>
        {
            b.From("direct:mandatory").Saga(s => s.Propagation(SagaPropagation.Mandatory).Compensation("mock:compensate"), body => body.Process(e => joined = SagaId(e)).To("mock:body"));
            b.From("direct:outer").Saga(_ => { }, body => body.Process(e => outer = SagaId(e)).To("direct:mandatory").ThrowException(new IOException()));
        });

        var alone = await Template.SendAsync("direct:mandatory", _ => { });

        var error = Assert.IsType<CaravanExchangeException>(alone.Exception);
        Assert.Contains("not bound to a saga", error.Message);
        Assert.Equal(0, Mock("body").ReceivedCounter);

        Mock("compensate").ExpectedMessageCount(1);
        await Template.SendAsync("direct:outer", _ => { });
        Assert.Equal(outer, joined);
        await Mock("compensate").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Supports_joins_an_existing_saga_and_runs_without_one_otherwise()
    {
        var seen = new List<string?>();
        await StartWithRoutes(b =>
        {
            b.From("direct:supports").Saga(s => s.Propagation(SagaPropagation.Supports).Compensation("mock:compensate"), body => body.Process(e => seen.Add(SagaId(e))));
            b.From("direct:outer").Saga(_ => { }, body => body.To("direct:supports").ThrowException(new IOException()));
        });

        var alone = await Template.SendAsync("direct:supports", _ => { });
        Assert.Null(alone.Exception);
        Assert.Null(seen.Single());
        Assert.Equal(0, Mock("compensate").ReceivedCounter);

        Mock("compensate").ExpectedMessageCount(1);
        await Template.SendAsync("direct:outer", _ => { });
        Assert.NotNull(seen[1]);
        await Mock("compensate").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Not_supported_suspends_the_current_saga_for_the_block()
    {
        string? inside = "unset";
        string? after = null;
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(
                s => s.Compensation("mock:outerCompensate"),
                body => body
                    .Saga(s => s.Propagation(SagaPropagation.NotSupported).Compensation("mock:notSupportedCompensate"), nested => nested.Process(e => inside = SagaId(e)))
                    .Process(e => after = SagaId(e))
                    .ThrowException(new IOException())));
        Mock("outerCompensate").ExpectedMessageCount(1);
        ExpectNone("notSupportedCompensate");

        await Template.SendAsync("direct:start", _ => { });

        Assert.Null(inside);
        Assert.NotNull(after);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Never_fails_with_a_saga_and_runs_without_one()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:never").Saga(s => s.Propagation(SagaPropagation.Never), body => body.To("mock:never"));
            b.From("direct:outer").Saga(s => s.Compensation("mock:compensate"), body => body.To("direct:never"));
        });
        Mock("compensate").ExpectedMessageCount(1);

        var alone = await Template.SendAsync("direct:never", _ => { });
        var inSaga = await Template.SendAsync("direct:outer", _ => { });

        Assert.Null(alone.Exception);
        var error = Assert.IsType<CaravanExchangeException>(inSaga.Exception);
        Assert.Contains("Never", error.Message);
        Assert.Equal(1, Mock("never").ReceivedCounter);
        await Mock("compensate").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Manual_saga_stays_open_until_saga_complete()
    {
        var service = UseService();
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .Saga(s => Step(s, "order").CompletionMode(SagaCompletionMode.Manual), body => body.To("mock:body"))
                .To("mock:after");
            b.From("direct:confirm").SagaComplete();
        });
        ExpectNone("compensate");

        var exchange = await Template.SendAsync("direct:start", _ => { });

        var id = SagaId(exchange);
        Assert.NotNull(id);
        Assert.Equal(id, SagaId(Mock("after").ReceivedExchanges.Single()));
        Assert.Equal(0, Mock("complete").ReceivedCounter);
        var saga = Assert.IsType<InMemorySagaCoordinator>(await service.GetSagaAsync(id));
        Assert.Equal(SagaStatus.Running, saga.Status);
        Assert.Equal(1, saga.StepCount);

        Mock("complete").ExpectedMessageCount(1);
        await Template.SendBodyAndHeaderAsync("direct:confirm", null, Lra, id);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(SagaStatus.Completed, saga.Status);
        Assert.Null(await service.GetSagaAsync(id));
    }

    [Fact]
    public async Task Manual_saga_is_compensated_with_saga_compensate()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(s => Step(s, "order").CompletionMode(SagaCompletionMode.Manual), body => body.To("mock:body"))
            .SagaCompensate()
            .To("mock:after"));
        Mock("compensate").ExpectedMessageCount(1);
        Mock("after").ExpectedMessageCount(1);
        ExpectNone("complete");

        var exchange = await Template.SendAsync("direct:start", _ => { });

        Assert.Null(exchange.Exception);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Saga_endpoint_uri_completes_a_manual_saga()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(s => Step(s, "order").CompletionMode(SagaCompletionMode.Manual), body => body.To("saga:complete")));
        Mock("complete").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("complete").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Completing_after_a_manual_compensation_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").Saga(s => Step(s, "order"), body => body.SagaCompensate()));
        Mock("compensate").ExpectedMessageCount(1);
        ExpectNone("complete");

        var exchange = await Template.SendAsync("direct:start", _ => { });

        var error = Assert.IsType<InvalidOperationException>(exchange.Exception);
        Assert.Contains("already compensated", error.Message);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Saga_component_fails_without_a_saga()
    {
        await StartWithRoutes(b => b.From("direct:start").SagaComplete());

        var exchange = await Template.SendAsync("direct:start", _ => { });
        var unknown = await Template.SendAsync("direct:start", e => e.Message.SetHeader(Lra, "unknown"));

        Assert.Contains("not bound to a saga", Assert.IsType<CaravanExchangeException>(exchange.Exception).Message);
        Assert.Contains("does not exist", Assert.IsType<CaravanExchangeException>(unknown.Exception).Message);
        Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("saga:other"));
    }

    [Fact]
    public async Task Saga_covering_the_rest_of_the_route_ends_with_the_exchange()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:ok").Saga(s => Step(s, "ok")).To("mock:ok");
            b.From("direct:fail").Saga(s => Step(s, "fail")).Process(e => Assert.NotNull(SagaId(e))).ThrowException(new IOException());
        });
        Mock("complete").ExpectedHeaderReceived("step", "ok");
        Mock("compensate").ExpectedHeaderReceived("step", "fail");

        var ok = await Template.SendAsync("direct:ok", _ => { });
        var failed = await Template.SendAsync("direct:fail", _ => { });

        Assert.NotNull(SagaId(ok));
        Assert.IsType<IOException>(failed.Exception);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Timeout_compensates_a_manual_saga_that_was_not_completed()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var service = UseService();
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(s => Step(s, "order").CompletionMode(SagaCompletionMode.Manual).Timeout(TimeSpan.FromMinutes(1)), body => body.To("mock:body")));
        Mock("compensate").ExpectedMessageCount(1);
        ExpectNone("complete");

        var exchange = await Template.SendAsync("direct:start", _ => { });
        var saga = (InMemorySagaCoordinator)(await service.GetSagaAsync(SagaId(exchange)!))!;
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(SagaStatus.Running, saga.Status);
        time.Advance(TimeSpan.FromSeconds(1));

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        await TestHelpers.WaitUntilAsync(() => saga.Status == SagaStatus.Compensated);
        var complete = await Template.SendAsync("saga:complete", e => e.Message.SetHeader(Lra, saga.Id));
        Assert.IsType<CaravanExchangeException>(complete.Exception);
    }

    [Fact]
    public async Task Timeout_compensates_a_running_auto_saga_and_its_completion_then_fails()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        UseService();
        var started = TestHelpers.NewSignal();
        var release = TestHelpers.NewSignal();
        await StartWithRoutes(b => b.From("direct:start")
            .Saga(s => Step(s, "order").Timeout(TimeSpan.FromSeconds(30)), body => body.Process(async _ =>
            {
                started.TrySetResult();
                await release.Task;
            })));
        Mock("compensate").ExpectedMessageCount(1);
        ExpectNone("complete");

        var send = Template.SendAsync("direct:start", _ => { });
        await started.Task.Bounded();
        time.Advance(TimeSpan.FromSeconds(30));
        await Mock("compensate").AssertIsSatisfiedAsync();
        release.TrySetResult();
        var exchange = await send.Bounded();

        Assert.IsType<InvalidOperationException>(exchange.Exception);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Completed_saga_does_not_time_out()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        UseService();
        await StartWithRoutes(b => b.From("direct:start").Saga(s => Step(s, "order").Timeout(TimeSpan.FromSeconds(30)), body => body.To("mock:body")));
        Mock("complete").ExpectedMessageCount(1);
        ExpectNone("compensate");

        await Template.SendBodyAsync("direct:start", "x");
        time.Advance(TimeSpan.FromMinutes(1));

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Compensation_calls_are_retried_with_a_delay()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        UseService(maxRetryAttempts: 3, retryDelay: TimeSpan.FromSeconds(2));
        var attempts = 0;
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Saga(s => s.Compensation("direct:compensate"), body => body.ThrowException(new IOException("fail")));
            b.From("direct:compensate").Process(_ =>
            {
                if (Interlocked.Increment(ref attempts) < 3)
                {
                    throw new InvalidOperationException("compensation service unavailable");
                }
            }).To("mock:compensated");
        });
        Mock("compensated").ExpectedMessageCount(1);

        var send = Template.SendAsync("direct:start", _ => { });
        await time.WaitForTimersAsync(1);
        Assert.Equal(1, Volatile.Read(ref attempts));
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Volatile.Read(ref attempts));
        time.Advance(TimeSpan.FromSeconds(2));
        var exchange = await send.Bounded();

        Assert.IsType<IOException>(exchange.Exception);
        Assert.Equal(3, attempts);
        await Mock("compensated").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Exhausted_compensation_retries_leave_the_saga_compensating()
    {
        var service = UseService(maxRetryAttempts: 2);
        var attempts = 0;
        InMemorySagaCoordinator? saga = null;
        await StartWithRoutes(b =>
        {
            b.From("direct:start").Saga(
                s => s.Compensation("direct:compensate"),
                body => body.Process(async e => saga = (InMemorySagaCoordinator?)await service.GetSagaAsync(SagaId(e)!)).ThrowException(new IOException("fail")));
            b.From("direct:compensate").Process(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("down");
            });
        });

        var exchange = await Template.SendAsync("direct:start", _ => { });

        Assert.IsType<IOException>(exchange.Exception);
        Assert.Equal(3, attempts);
        Assert.Equal(SagaStatus.Compensating, saga!.Status);
        Assert.Empty(service.Sagas);
    }

    [Fact]
    public async Task Default_saga_service_is_created_registered_and_stopped_with_the_context()
    {
        await StartWithRoutes(b => b.From("direct:start").Saga(_ => { }, body => body.To("mock:body")));

        var service = Assert.IsType<InMemorySagaService>(Context.GetSagaService());
        Assert.Equal(ServiceStatus.Started, service.Status);
        Assert.Same(service, Context.GetSagaService());

        await Context.StopAsync();
        Assert.Equal(ServiceStatus.Stopped, service.Status);
    }

    [Fact]
    public async Task Saga_service_can_come_from_the_registry()
    {
        var service = new InMemorySagaService(Context);
        Context.Registry.Bind("sagas", service);
        await StartWithRoutes(b => b.From("direct:start").Saga(_ => { }, body => body.To("mock:body")));

        Assert.Same(service, Context.GetSagaService());
        Assert.Equal(ServiceStatus.Started, service.Status);
    }

    [Fact]
    public async Task Begin_step_on_a_finished_saga_fails()
    {
        var service = UseService();
        var exchange = new Exchange(Context);
        var saga = await service.NewSagaAsync(exchange);
        await saga.CompleteAsync(exchange);

        await saga.CompleteAsync(exchange);
        await Assert.ThrowsAsync<InvalidOperationException>(() => saga.BeginStepAsync(exchange, new SagaStep(null, null, new Dictionary<string, IExpression>(), null)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => saga.CompensateAsync(exchange));
    }

    [Fact]
    public void Chained_saga_cannot_have_nested_steps()
    {
        var definition = new SagaDefinition { ScopeIsRestOfRoute = true };
        definition.Outputs.Add(new ToDefinition("mock:x"));

        var e = Assert.Throws<InvalidOperationException>(() => definition.CreateProcessor(new RouteContext(Context, new RouteDefinition("direct:start"), "r")));

        Assert.Contains("rest of the route", e.Message);
    }
}
