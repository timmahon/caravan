# Caravan architecture

This document describes how the core is put together and the conventions every feature follows.
Read it before adding an EIP or a component.

## Runtime model

```
Consumer ──> Route.Processor (RouteProcessor: unit of work, inflight, route id)
               └─> Pipeline
                     ├─> Channel ─> [intercept strategies] ─> [error handler] ─> node processor
                     ├─> Channel ─> ...
                     └─> Channel ─> ...
```

| Type | Role | Camel equivalent |
|---|---|---|
| `ICaravanContext` / `CaravanContext` | Components, endpoints, routes, registry, type converters, lifecycle | `CamelContext` |
| `Exchange` | One message exchange: `Message`, `Properties`, `Pattern`, `Exception`, `UnitOfWork` | `Exchange` |
| `Message` | Body + case-insensitive headers. `GetBody<T>()` converts with the context's type converter | `Message` |
| `IProcessor` | `ValueTask ProcessAsync(Exchange)` | `AsyncProcessor` |
| `IComponent` / `IEndpoint` / `IProducer` / `IConsumer` | Component SPI | same |
| `IExpression` / `IPredicate` | Evaluate values / conditions on an exchange. `Expr` has factories | `Expression` / `Predicate` |
| `ProcessorDefinition` | Node in the route model (`Caravan.Model`) | `ProcessorDefinition` |
| `RouteDefinition` | A route: `FromUri` + `Outputs` + route options | `RouteDefinition` |
| `RouteContext` | Builds processors from definitions; owns route services; wraps nodes | reifiers + `Route` |
| `Channel` | Wraps every node: catches exceptions into `exchange.Exception`, skips stopped exchanges | `DefaultChannel` |
| `IInterceptStrategy` | Wraps every node (tracing, metrics, interception, advice) | `InterceptStrategy` |
| `IErrorHandlerFactory` | Wraps every node with error handling (set on `RouteContext.ErrorHandlerFactory`) | `ErrorHandler` |
| `IUnitOfWork` / `ISynchronization` | Completion callbacks per exchange | `UnitOfWork` / `Synchronization` |
| `IEventNotifier` | Context, route and exchange events | `EventNotifier` |
| `IRegistry` | Named/typed beans, optionally backed by `IServiceProvider` | `Registry` |
| `TypeConverterRegistry` | Type conversions; add custom ones with `AddConverter` | `TypeConverter` |
| `IProducerTemplate` | Send/request from application code | `ProducerTemplate` |

### Error semantics

- Processors **may throw**. `Channel` catches the exception and sets `exchange.Exception`.
- `Pipeline` stops when `exchange.IsFailed`, `exchange.IsRouteStop` or `exchange.IsRollbackOnly`.
- `RouteProcessor` creates the unit of work only if the exchange has none (so `direct` calls between routes share one).
- `IProducerTemplate.Send*Body*` / `Request*` throw `CaravanExecutionException` when the exchange failed;
  `SendAsync(uri, exchange)` returns the exchange with `Exception` set instead.

### Endpoint URIs

- `scheme:path?k=v&k2=v2` (also `scheme://path`). Values may use `RAW(...)` to avoid decoding.
- `{{key}}` / `{{key:default}}` placeholders are resolved via `ICaravanContext.PropertiesSources`.
- Endpoints are cached by normalized URI (parameters sorted).
- Remaining URI parameters are bound to public settable properties of the endpoint (case-insensitive,
  converted by the type converter). `#name` / `#bean:name` values are looked up in the registry.
  Unknown parameters fail unless the endpoint is lenient (`IsLenientProperties`).
- Built-in schemes are registered with `CaravanContext.RegisterGlobalComponent` in the static constructor;
  per-context with `AddComponent` / `AddComponentFactory`; or by binding an `IComponent` in the registry
  under the scheme name.

### Extension points for features

Optional features hook into the core without changing it:

- `ICaravanContext.GetOrAddExtension<T>()` / `SetExtension<T>()` – store feature state on the context.
- `RouteContextConfigurers` (context extension) – callbacks run for each route before its processors
  are built (e.g. to set `RouteContext.ErrorHandlerFactory`).
- `RouteBuilderHooks` (context extension) – post-process route definitions after `Configure()`.
- `RouteDefinition.Extensions` – feature-specific per-route configuration keyed by type.
- `ICaravanContext.InterceptStrategies` / `RouteContext.InterceptStrategies` – wrap nodes.

## Adding an EIP

Each EIP is four pieces, each in its own file named after the EIP where reasonable:

1. **Definition** in `src/Caravan.Core/Model/` – a `ProcessorDefinition` (or `OutputDefinition` when it has
   a nested block). Plain settable properties, a `ShortName` matching Camel's EIP name (`split`, `choice`, ...),
   a `Label`, and `CreateProcessor(RouteContext)`. Expose nested blocks via `Children` so
   `RouteDefinition.AllNodes()` sees them. Build nested blocks with `routeContext.CreateBlockProcessor(outputs, this)`.
2. **Processor** in `src/Caravan.Core/Processors/`. If it owns producers, threads or timers, derive from
   `ServiceSupport` and the route will start/stop it (`RouteContext.AddService` happens automatically for the
   node processor; call it yourself for extra objects). Implement `INavigableProcessor` when it wraps others.
3. **DSL** in `src/Caravan.Core/Builder/` – generic extension methods on `T : RouteSteps` that return `T`.
   Nested blocks take `Action<RouteSteps>` lambdas, e.g.
   `.Choice(c => c.When(p, r => r.To("mock:a")).Otherwise(r => r.To("mock:b")))`.
   Offer typed overloads where useful (`Func<TBody, bool>` predicates etc.); users write explicitly typed
   lambdas like `.Filter((Order o) => o.Total > 0)` so C# can infer the type parameters.
4. **Tests** in `tests/Caravan.Core.Tests/` using `ContextTestBase` and mock endpoints.

Use exchange property names from `ExchangePropertyKeys` (add new ones there) and keep Camel's semantics
unless there is a documented reason to differ (note it in the definition's XML doc).

## Adding a component

Derive from `ComponentBase` (override `CreateEndpoint(uri, remaining, parameters)`), `EndpointBase`
(public settable properties = URI options), `ProducerBase` and/or `ConsumerBase`. Consumers call
`Processor.ProcessAsync(exchange)` with exchanges created by `Endpoint.CreateExchange()`.
Register built-in components in the `CaravanContext` static constructor.

## Time

Use `context.TimeProvider` for anything time-based (delays, timers, timeouts) so tests can use a fake clock.

## Conventions

- `net10.0`, nullable enabled, warnings are errors, XML docs on public types where they add information.
- File-scoped namespaces: `Caravan` (main API), `Caravan.Spi`, `Caravan.Model`, `Caravan.Builder`,
  `Caravan.Processors`, `Caravan.Support`, `Caravan.Impl`, `Caravan.Components.<Name>`.
- Tests: xUnit, one test class per feature, descriptive `Snake_case_names`. No sleeps where a mock
  expectation or `TimeProvider` can be used instead; keep the suite fast.
