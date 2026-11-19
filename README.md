# Caravan

A reimagining of [Apache Camel](https://camel.apache.org) for .NET: an integration framework built around
the Enterprise Integration Patterns, with routes defined in a fluent C# DSL.

```csharp
using static Caravan.Builder.Lang;

await using var context = new CaravanContext();
context.PropertiesComponent.AddConfiguration(configuration);      // {{placeholders}} from appsettings.json, env, ...

context.AddRoutes(RouteBuilder.Create(b =>
    b.From("direct:orders")
     .RouteId("orders")
     .SetHeader("received", Constant(true))
     .Filter(Simple("${body.Total} > {{orders.threshold:100}}"), big => big
         .SetHeader("customer", Simple("${body.Customer?.Name ?: 'unknown'}"))
         .Log("Big order ${body.Id} from ${header.customer} at ${date:now:HH:mm:ss}")
         .ToD("seda:{{orders.queue}}-${header.customer}"))
     .Transform((Order o) => o with { Checked = true })     // or plain C# lambdas, typed
     .To("mock:result")));

await context.StartAsync();

var template = context.CreateProducerTemplate();
await template.SendBodyAsync("direct:orders", new Order("o1", 250m));
```

Expressions can be C# lambdas or Camel's [Simple language](docs/ARCHITECTURE.md#languages-and-the-simple-language)
(`${header.x}`, operators, OGNL, `${date:...}`, ternary/elvis), compiled once and cached; property placeholders (`{{key}}`) resolve from
`IConfiguration`, dictionaries or environment variables.

## YAML routes

`Caravan.Dsl.Yaml` loads routes written in [Camel's YAML DSL](https://camel.apache.org/components/latest/others/yaml-dsl.html), so
existing Camel YAML files mostly load unchanged: every EIP, route templates, `templatedRoute`, `routeConfiguration`, `beans`, error
handlers and interceptors, validated strictly (unknown steps or options fail with the file, line and column) and described by a JSON schema
([`schema/caravan-yaml-dsl.json`](schema/caravan-yaml-dsl.json)).

```yaml
# routes/orders.yaml
- beans:
    - name: totals
      type: MyApp.TotalsAggregator, MyApp
- route:
    id: orders
    from:
      uri: direct:orders
      steps:
        - setHeader: { name: received, constant: "true" }
        - choice:
            when:
              - simple: "${body.Total} > {{orders.threshold:100}}"
                steps:
                  - to: { uri: "seda:big", parameters: { size: 1000 } }
            otherwise:
              steps: [ { to: "seda:small" } ]
        - split:
            tokenize: ","
            aggregationStrategy: totals
            parallelProcessing: true
            steps: [ { to: "direct:line" } ]
```

```csharp
context.AddRoutes(YamlRoutesBuilder.FromFile("routes/orders.yaml"));         // or FromString / FromStream
new RoutesLoader(context, new() { Watch = true }).Load("routes/**/*.yaml");   // a directory or glob, reloading changed files
builder.Services.AddCaravan().AddYamlRoutes("routes/*.yaml");                // Caravan.Hosting
```

## REST services on ASP.NET Core

The REST DSL (Camel's `rest`) describes HTTP services independently of the transport; `Caravan.AspNetCore` serves them, and plain
`platform-http:` routes, as ASP.NET Core endpoints of a minimal-API application, binds JSON/XML bodies to your types and publishes an OpenAPI
document.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCaravan().AddAspNetCore().AddRoutes<OrderApi>();
var app = builder.Build();
app.MapCaravan(o => o.Management = true);   // REST + platform-http endpoints, /caravan/health, /caravan/management, OpenAPI
app.Run();

public sealed class OrderApi(IOrderStore store) : RouteBuilder
{
    public override void Configure()
    {
        RestConfiguration(c => c
            .BindingMode(RestBindingMode.Json)      // request bodies -> Type, responses -> JSON
            .ClientRequestValidation()              // 415 / 406 / 400 before your route runs
            .ApiContextPath("/api-doc")             // OpenAPI 3.1 document
            .ApiProperty("api.title", "Orders"));

        Rest("/api/orders", r => r
            .Get("/{id}", v => v.Produces("application/json").OutType<Order>()
                .Param("id", RestParamType.Path, p => p.DataType("integer"))
                .To("direct:getOrder"))
            .Post(v => v.Type<Order>().OutType<Order>().To("direct:createOrder")));

        From("direct:getOrder")                     // merged into the GET service (inlineRoutes)
            .Process(e => e.Message.Body = store.Find(e.Message.GetHeader<int>("id")));
        From("direct:createOrder")
            .Process(e => e.Message.Body = store.Add(e.Message.GetBody<Order>()!))
            .SetHeader(CaravanHeaders.HttpResponseCode, Expr.Constant(201));

        From("platform-http:/hooks/{source}?httpMethodRestrict=POST")   // any route can be an HTTP endpoint
            .To("seda:hooks");
    }
}
```

Starting or stopping a route adds or removes its endpoint at once; the same services can be written in YAML (`- rest:` /
`- restConfiguration:`).

## Hosting and observability

`Caravan.Hosting` runs a context in the .NET Generic Host: route builders come from dependency injection, options from the `Caravan`
configuration section, and the context starts and stops (gracefully) with the host.

```csharp
var builder = Host.CreateApplicationBuilder(args);          // appsettings.json: { "Caravan": { "Name": "orders", "MessageHistory": true } }
builder.Services.AddSingleton<IOrderStore, SqlOrderStore>();
builder.Services.AddCaravan(o => o.ShutdownTimeout = TimeSpan.FromSeconds(20))
    .AddRoutes<OrderRoutes>()                                // constructor-injected: OrderRoutes(IOrderStore store)
    .AddRouteConfiguration<ErrorConfigurations>();
builder.Services.AddHealthChecks().AddCaravanHealthChecks(); // caravan-context (live), caravan-routes, -consumers, ... (ready)
builder.Services.AddOpenTelemetry()                         // Caravan.OpenTelemetry
    .WithTracing(t => t.AddCaravanInstrumentation())         // or AddSource("Caravan")
    .WithMetrics(m => m.AddCaravanInstrumentation());        // or AddMeter("Caravan")
await builder.Build().RunAsync();                            // IProducerTemplate is injectable anywhere
```

Spans follow exchanges across routes, `seda` hops and transports (W3C `traceparent` headers); `context.Management` gives route and node
statistics and operations, `context.Tracing`/`BacklogTracer` trace every step, and `MessageHistory` adds the route/node table to error logs.

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
