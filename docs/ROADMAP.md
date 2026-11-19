# Caravan roadmap

> **Status: complete.** All 17 PRs below are merged. What remains to reach full Camel-core parity — and what is deliberately not
> applicable on .NET — is tracked row by row in [PARITY.md](PARITY.md); connectors come next, on the same component SPI.

Caravan is a reimagining of [Apache Camel](https://camel.apache.org) for .NET.
The goal of this roadmap is **parity with Camel core**: the routing engine, the
Enterprise Integration Patterns (EIPs), error handling, the core components
(`direct`, `seda`, `timer`, `scheduler`, `log`, `mock`, `bean`, `file`, ...),
the expression language, DSLs, testing support and management.
Connectors to third-party systems (Kafka, JMS, AWS, ...) are out of scope here
and will be built later on top of the same component SPI.

## Design principles

| Camel | Caravan | Why |
|---|---|---|
| `CamelContext` + its own registry | `CaravanContext`, optionally backed by `IServiceProvider` | Fits the Generic Host and DI; still usable standalone. |
| `Exchange` with `Object` body + type converters | Same runtime model (needed for dynamic EIPs and YAML), **plus generic overloads** (`Process<T>`, `Transform<TIn,TOut>`, `Filter<T>`, `GetBody<T>()`) | Keep Camel's flexibility, give C# users compile-time types where they write code. |
| Callback-based async routing engine | `ValueTask`-based `IProcessor`, `System.Threading.Channels` for SEDA | Native async/await, backpressure, cancellation. |
| Java DSL `.choice().when()...end()` | Nested lambdas: `.Choice(c => c.When(p, r => r.To(...)).Otherwise(...))` | No dangling `.end()`; blocks are scoped by the compiler. |
| Definition model + reifiers | Same: a `ProcessorDefinition` tree, turned into processors at start | Needed for YAML, node ids, interceptors and AdviceWith. |
| Hystrix/Resilience4j circuit breaker | Polly v8 | The .NET standard. |
| JMX management | OpenTelemetry traces/metrics + health checks + a management API | Cloud-native observability. |
| `Thread.sleep` / real clocks in tests | `TimeProvider` everywhere | Deterministic tests for timers, delays, timeouts, aggregation. |
| JTA transactions | Unit-of-work synchronizations + outbox-friendly hooks | No XA in .NET. |
| Simple language | Simple-compatible expression language (for YAML/dynamic URIs); C# lambdas preferred in code | Familiar to Camel users, safe for config files. |

Conventions:

- Target `net10.0`, nullable enabled, warnings as errors.
- Processors may throw; the route channel catches and records the exception on the exchange (`exchange.Exception`).
- Every EIP has a definition class (`Caravan.Model`), a processor (`Caravan.Processors`), a DSL extension method on `RouteSteps`, and tests.
- Each PR builds on the previous one, compiles, and has all tests green.

## Stacked PRs

| # | PR | Camel features covered |
|---|---|---|
| 1 | **Foundation** | Solution + CI; `CaravanContext`, `Exchange`, `Message`, exchange pattern, `IProcessor`; component/endpoint/producer/consumer SPI; URI parsing and endpoint option binding; registry; type converters; definition model + route builder; pipeline; `process`, `to`, `setBody`, `transform`, `setHeader`; `direct`, `log`, `mock` components; `ProducerTemplate`. |
| 2 | **Expressions & basic EIPs** | Expression/predicate model and builders; `filter`, `choice`, `stop`, `toD`, `setProperty`, `removeHeader(s)`, `removeProperty`, `convertBodyTo`, `log` EIP, `wireTap`, `pipeline`, `step`; generic typed overloads. |
| 3 | **Async runtime & route control** | `seda` (channels, concurrent consumers, blocking/backpressure, `waitForTaskToComplete`), `timer`, `scheduler`; polling consumer + `ConsumerTemplate`; route controller (start/stop/suspend/resume/remove, `autoStartup`, `startupOrder`); graceful shutdown; inflight repository; event notifier. |
| 4 | **Error handling** | `DefaultErrorHandler`, `DeadLetterChannel`, `NoErrorHandler`; redelivery policy (max, delay, backoff, `retryWhile`, `useOriginalMessage`); `onException` (route/global, `handled`, `continued`, `onWhen`, exception-type matching); `doTry/doCatch/doFinally`; `throwException`; `onCompletion`; unit-of-work synchronizations. |
| 5 | **Split, multicast, recipient list** | `split` (iterable/streaming/tokenize, parallel, `stopOnException`, aggregation, split properties), `multicast`, `recipientList`, aggregation strategies, parallel processing with bounded concurrency. |
| 6 | **Aggregator & resequencer** | `aggregate` (correlation, completion size/timeout/interval/predicate/from-batch-consumer, eager check, `closeCorrelationKeyOnCompletion`, discard), `IAggregationRepository` (in-memory), `resequence` (batch + stream). |
| 7 | **Routing & flow-control EIPs** | `routingSlip`, `dynamicRouter`, `enrich`, `pollEnrich`, `loop` (count/while/copy), `delay`, `throttle`, `sample`, `idempotentConsumer` (+ repository), `loadBalance` (round-robin, random, weighted, failover, sticky, topic), `claimCheck`, `validate`. |
| 8 | **Resilience & sagas** | `circuitBreaker` with fallback (Polly), `saga` EIP with in-memory saga service (compensation/completion, propagation modes). |
| 9 | **Interception & testing** | `intercept`, `interceptFrom`, `interceptSendToEndpoint`; `Caravan.Testing`: AdviceWith (`weaveById/ToUri/ByType`, `replaceFromWith`, `mockEndpoints`), `NotifyBuilder`, test base class, mock endpoint expectations completeness. |
| 10 | **Simple language & properties** | Simple language (`${body}`, `${header.x}`, operators, functions, `${date:...}`, OGNL-lite); `simple()` and dynamic URIs; properties component with `{{placeholder}}` resolution from `IConfiguration`. |
| 11 | **Data formats, bean & misc components** | `marshal`/`unmarshal` (JSON, XML, CSV, Base64, GZip, string), stream caching; `bean` component + bean EIP with method binding; `controlbus`, `stub`, `ref`, `dataset`. |
| 12 | **Route templates & configuration** | Route templates and template parameters (Kamelet equivalent), `routeConfiguration`, global configuration. |
| 13 | **Hosting & observability** | `Caravan.Hosting` (`AddCaravan`, hosted service, DI-backed registry, health checks); OpenTelemetry tracing + metrics; message history, tracer, route statistics, management API. |
| 14 | **YAML DSL** | YAML route loader covering all EIPs, JSON schema generation. |
| 15 | **File component** | `file` consumer/producer: polling, include/exclude, move/delete/noop, idempotent, read locks, file name expressions, append/override strategies. |
| 16 | **REST DSL** | REST DSL on ASP.NET Core, `platform-http` component, binding modes. |
| 17 | **Parity audit** | `docs/PARITY.md` feature matrix against Camel core; gaps closed: Camel 4 variables (`setVariable(s)`, `removeVariable`, `convertVariableTo`, `variableSend`/`variableReceive`, `${variable.x}`, `variable` language), route policies (`ThrottlingInflightRoutePolicy`, `ThrottlingExceptionRoutePolicy`), `threads` and `poll` EIPs, route `precondition` and `nodePrefixId`, data type transformers/validators with `inputType`/`outputType` and `transform` `toType`, consuming from Kamelets, the `language` component, `${messageTimestamp}`; samples; NuGet packaging. |
