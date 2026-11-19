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
| `IConsumerTemplate` | Receive (poll) from application code; `DoneUoWAsync` completes a received exchange | `ConsumerTemplate` |
| `IPollingConsumer` / `IPollingConsumerEndpoint` | Pull-model consumers; `PollingConsumerFactory.Create` polls any endpoint | `PollingConsumer` |
| `IInflightRepository` | Exchanges being routed, per route, with durations (fed by `RouteProcessor`) | `InflightRepository` |
| `IShutdownStrategy` / `IShutdownAware` | Graceful stop/suspend of routes; consumers that queue or loop take part | `ShutdownStrategy` / `ShutdownAware` |
| `SupervisingRouteController` | Retries routes that fail to start, with backoff | `SupervisingRouteController` |

### Error semantics

- Processors **may throw**. `Channel` catches the exception and sets `exchange.Exception`.
- `Pipeline` stops when `exchange.IsFailed`, `exchange.IsRouteStop` or `exchange.IsRollbackOnly`.
- `RouteProcessor` creates the unit of work only if the exchange has none (so `direct` calls between routes share one).
  Callbacks added with `Exchange.AddOnCompletion` before a unit of work exists are taken over by it. When an exchange is
  handed to another task (e.g. `seda`), its synchronizations are handed over to the copy unless
  `ISynchronization.AllowHandover` is false.
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

Each EIP is four pieces, each type in its own file named after the type:

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
   When a step needs a type that cannot be inferred from an argument (e.g. `convertBodyTo`), take a `Type`
   parameter (`.ConvertBodyTo(typeof(int))`): C# cannot infer `T` from the receiver while the caller gives
   another type argument explicitly, and C# 14 extension blocks do not help either, because an explicit type
   argument binds to the block's own type parameter. Options go in optional named parameters
   (`.ToD(uri, cacheSize: 10)`). Add a lambda-shape case to `DslOverloadTests` for every new overload set, so
   that an ambiguity such as `_ => throw ...` matching two overloads fails the build.
4. **Tests** in `tests/Caravan.Core.Tests/` using `ContextTestBase` and mock endpoints.

Use exchange property names from `ExchangePropertyKeys` (add new ones there) and keep Camel's semantics
unless there is a documented reason to differ (note it in the definition's XML doc).

Expressions and predicates: `Expr` (values), `Pred` (predicate factories and combinators) and the
`PredicateExtensions` on `IExpression` (`IsEqualTo`, `IsGreaterThan`, `Contains`, `Regex`, `In`, ...), which
coerce types like Camel (`Support.ObjectHelper`). String options that accept `${...}` tokens (`toD`, `wireTap`,
the `log` EIP) go through `Support.TokenTemplate`, an interim stand-in to be replaced by the Simple language.

## Adding a component

Derive from `ComponentBase` (override `CreateEndpoint(uri, remaining, parameters)`), `EndpointBase`
(public settable properties = URI options), `ProducerBase` and/or `ConsumerBase`. Consumers call
`Processor.ProcessAsync(exchange)` with exchanges created by `Endpoint.CreateExchange()`.
Register built-in components in the `CaravanContext` static constructor. Background consumers follow the rules in
"Consumers: lifecycle, suspension and shutdown" above.

Built-in components: `direct`, `seda` (and `vm`, its process-wide variant), `timer`, `scheduler`, `log`, `mock`.

### Consumers: lifecycle, suspension and shutdown

- Routes are controlled through the context: `StartRouteAsync`, `StopRouteAsync`, `SuspendRouteAsync`,
  `ResumeRouteAsync`, `RemoveRouteAsync`, `GetRouteStatus`. Stop and suspend go through `ICaravanContext.ShutdownStrategy`
  (default `DefaultShutdownStrategy`, timeout 45s): consumers stop first, deferred ones (`ShutdownRoute.Defer`, or
  `IShutdownAware.DeferShutdown`) keep running, inflight exchanges are awaited, then deferred consumers stop. On timeout
  shutdown-aware consumers are forced (`PrepareShutdown(..., forced: true)`) and, on context stop,
  `ICaravanContext.ShutdownToken` is cancelled. Context stop then stops the routes' services, endpoints and components.
- A consumer that can pause overrides `ServiceSupport.DoSuspendAsync`/`DoResumeAsync` (the default suspends by stopping).
  Suspension stops taking new work and waits for the current work (bounded by the forced shutdown).
- A consumer that queues messages or runs its own loop implements `IShutdownAware`: report queued messages in
  `PendingExchangesSize` (drained with `ShutdownRunningTask.CompleteAllTasks`) and stop waiting / cancel work when forced.
  Run background loops on `Task.Run`; `DoStopAsync` cancels them and awaits them unless forced.
- Exchanges created by background consumers use a token linked to the consumer's forced shutdown and
  `ShutdownToken`; `RouteProcessor` defaults `exchange.CancellationToken` to `ShutdownToken`.
- Polling: override `EndpointBase.CreatePollingConsumer` when the component has a natural pull model (seda); otherwise
  `EventDrivenPollingConsumer` adapts the event-driven consumer. Components that poll a resource on a schedule derive
  their consumer from `ScheduledPollConsumer` (implement `PollAsync`, return the number of messages processed) and their
  endpoint from `ScheduledPollEndpoint` (scheduling options as URI options, `ConfigureConsumer`).
- `SupervisingRouteController` (set `ICaravanContext.SupervisingRouteController` before start) turns route start failures
  into background retries; failed routes keep status `Failed` and are listed in `FailedRoutes`.

## Time

Use `context.TimeProvider` for anything time-based (delays, timers, timeouts) so tests can use a fake clock.
Tests use `TestTimeProvider` (a `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` that counts created
timers): `AdvanceAndWaitForTimerAsync` advances the clock and waits until the woken code has scheduled its next timer,
so loops driven by `Task.Delay(…, TimeProvider)` are tested without sleeps or missed advances. Code that waits for
something with a timeout must cancel the wait itself on timeout (a `CancellationTokenSource(timeout, timeProvider)`
linked to the caller's token) rather than abandon it, so a late result is not taken and lost.

## Conventions

Ground rules, enforced by the build (a violation fails `dotnet build`):

1. **Central package management** – versions live only in `Directory.Packages.props`; `PackageReference`
   items never carry a `Version`.
2. **Warnings are errors** – `TreatWarningsAsErrors` in `Directory.Build.props`.
3. **One type per file, file named after the type** – StyleCop.Analyzers rules SA1402 and SA1649 at error
   (`.editorconfig`, `stylecop.json`); every other StyleCop rule is off. In the `src` projects this applies to all top-level types
   (classes, records, interfaces, structs, enums, delegates), internal ones included.
4. **XML documentation on every public type and public/protected member** of the `src` projects – CS1591 is
   not suppressed. Write a useful `<summary>` (plus `<param>`/`<returns>`/`<exception>` where they add
   information); use `<inheritdoc />` for overrides and interface implementations.

Other conventions:

- `net10.0`, nullable enabled.
- File-scoped namespaces: `Caravan` (main API), `Caravan.Spi`, `Caravan.Model`, `Caravan.Builder`,
  `Caravan.Processors`, `Caravan.Support`, `Caravan.Impl`, `Caravan.Components.<Name>`.
- Tests: xUnit, one test class per feature, descriptive `Snake_case_names`. No sleeps where a mock
  expectation or `TimeProvider` can be used instead; keep the suite fast.
