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
