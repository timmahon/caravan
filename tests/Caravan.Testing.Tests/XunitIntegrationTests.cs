using Caravan.Builder;

namespace Caravan.Testing.Tests;

/// <summary>A test class deriving from <see cref="CaravanTestSupport"/>, as in the README.</summary>
public sealed class XunitIntegrationTests : CaravanTestSupport, IAsyncLifetime
{
    protected override bool UseAdviceWith => true;

    Task IAsyncLifetime.InitializeAsync() => SetUpAsync();

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    [Fact]
    public async Task Advised_route_is_tested_with_mocks_and_notify()
    {
        await AdviceWithAsync("orders", a =>
        {
            a.ReplaceFromWith("direct:orders");
            a.MockEndpointsAndSkip("seda:billing");
            a.WeaveAddLast().To("mock:result");
        });
        await StartAsync();
        var result = GetMockEndpoint("mock:result");
        result.ExpectedMessageCount(2);
        result.AllMessages().Header("total").IsGreaterThan(0);
        GetMockEndpoint("mock:seda:billing").ExpectedBodiesReceived("A", "B");
        using var notify = new NotifyBuilder(Context).FromRoute("orders").WhenCompleted(2).Create();

        await Template.SendBodyAndHeaderAsync("direct:orders", "a", "total", 10);
        await Template.SendBodyAndHeaderAsync("direct:orders", "b", "total", 20);

        Assert.True(await notify.MatchesAsync(TimeSpan.FromSeconds(10)));
        await AssertMockEndpointsSatisfiedAsync();
    }

    protected override IRoutesBuilder CreateRouteBuilder() => RouteBuilder.Create(b => b
        .From("timer:orders?period=60000").RouteId("orders")
        .Transform(e => e.Message.GetBody<string>()!.ToUpperInvariant())
        .To("seda:billing"));
}
