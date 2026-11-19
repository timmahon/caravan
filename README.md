# Caravan

A reimagining of [Apache Camel](https://camel.apache.org) for .NET: an integration framework built around
the Enterprise Integration Patterns, with routes defined in a fluent C# DSL.

```csharp
await using var context = new CaravanContext();

context.AddRoutes(RouteBuilder.Create(b =>
    b.From("direct:orders")
     .RouteId("orders")
     .SetHeader("received", Expr.Constant(true))
     .Transform((string s) => s.ToUpperInvariant())
     .To("log:orders?showHeaders=true")
     .To("mock:result")));

await context.StartAsync();

var template = context.CreateProducerTemplate();
await template.SendBodyAsync("direct:orders", "hello");
```

## Testing your routes

`Caravan.Testing` (no test framework dependency) brings Camel's test kit: a base class, `AdviceWith` to change routes for a
test, endpoint mocking, `NotifyBuilder` to wait for asynchronous routes, and fluent mock expectations.

```csharp
public sealed class OrderRoutesTests : CaravanTestSupport, IAsyncLifetime
{
    protected override bool UseAdviceWith => true;           // advise before the context starts

    protected override IRoutesBuilder CreateRouteBuilder() => new OrderRoutes();

    Task IAsyncLifetime.InitializeAsync() => SetUpAsync();

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    [Fact]
    public async Task Bills_each_order()
    {
        await AdviceWithAsync("orders", a =>
        {
            a.ReplaceFromWith("direct:orders");                 // instead of the production consumer
            a.MockEndpointsAndSkip("seda:billing");             // sent to mock:seda:billing only
            a.WeaveById("audit").Replace(r => r.To("mock:audit"));
        });
        await StartAsync();

        var billing = GetMockEndpoint("mock:seda:billing");
        billing.ExpectedMessageCount(2);
        billing.Message(0).Header("total").IsGreaterThan(0);
        using var notify = new NotifyBuilder(Context).FromRoute("orders").WhenDone(2).Create();

        await Template.SendBodyAndHeaderAsync("direct:orders", "a", "total", 10);
        await Template.SendBodyAndHeaderAsync("direct:orders", "b", "total", 20);

        Assert.True(await notify.MatchesAsync(TimeSpan.FromSeconds(10)));
        await AssertMockEndpointsSatisfiedAsync();
    }
}
```

## Status

Early development. See [docs/ROADMAP.md](docs/ROADMAP.md) for the plan to reach parity with Camel core,
and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the core is put together.

## Build

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet test
```

## License

Apache License 2.0.
