# Caravan ↔ Apache Camel core: parity matrix

This is the definitive record of how Caravan compares with **Apache Camel 4.x core** — the routing engine, the EIPs, the components that ship in
`camel-core` (plus `platform-http`, `rest` and the YAML DSL), the core languages and data formats, error handling, testing and management.
Connectors to third-party systems (Kafka, JMS, AWS, HTTP clients, ...) are out of scope: they will be built on the same component SPI.

Every row links to the section of [ARCHITECTURE.md](ARCHITECTURE.md) that documents the behaviour and its deviations. "Partial" always says
what is missing; anything not listed under a 🟡 row's notes behaves as in Camel.

| Status | Meaning |
|---|---|
| ✅ | Parity: Camel's semantics and options (documented deviations are small and deliberate) |
| 🟡 | Partial: the feature exists, notable options or behaviours are missing |
| ❌ | Not implemented (yet) |
| ➖ | Not applicable on .NET, or replaced by the .NET equivalent on purpose |

## Summary

| Group | ✅ | 🟡 | ❌ | ➖ |
|---|---|---|---|---|
| [EIPs](#eips) | 57 | 2 | 1 | 6 |
| [Core components](#core-components) | 19 | 3 | 3 | 2 |
| [Languages](#languages) | 9 | 1 | 1 | 3 |
| [Data formats](#data-formats) | 7 | 0 | 1 | 2 |
| [DSLs](#dsls) | 3 | 1 | 1 | 0 |
| [Error handling & transactions](#error-handling-and-transactions) | 11 | 0 | 0 | 2 |
| [Routes: lifecycle, policies, configuration](#routes-lifecycle-policies-and-configuration) | 17 | 0 | 0 | 1 |
| [Templates & Kamelets](#route-templates-and-kamelets) | 5 | 1 | 1 | 0 |
| [Data types: transformers & validators](#data-types-transformers-and-validators) | 6 | 0 | 1 | 0 |
| [Variables](#variables) | 7 | 0 | 0 | 0 |
| [Properties & configuration](#properties-and-configuration) | 7 | 1 | 1 | 0 |
| [Testing](#testing) | 8 | 1 | 1 | 1 |
| [Management, observability, health](#management-observability-and-health) | 7 | 1 | 3 | 1 |
| [Runtime: threading, shutdown, type converters](#runtime-threading-shutdown-and-type-converters) | 8 | 1 | 0 | 4 |
| **Total** | **171** | **12** | **14** | **22** |

The most notable remaining gaps: ❌ the **XML DSL**, **`jsonpath`/`xpath`** languages, the **`validator` (XSD)** component, the `properties:`
endpoint, **Kamelet YAML resource specs**, the **developer console** and the **debugger**, encrypted properties, the startup recorder, Camel's
resume strategies, `loadTransformer` and `zipFile`; 🟡 the **Simple functions added through Camel 4.x** (`replace`, `substring*`,
`uppercase`, `iif`, `sum`, ...), a round-tripping **route dumper** (only a text dump exists), the **REST producer**, a few `file` options,
`threads` executor references, `variableSend` on `wireTap`, the `policy` EIP and MDC logging.

---

## EIPs

Camel's EIP index (Camel 4.x documentation), in its order. Messaging-system patterns that are concepts rather than DSL elements (Message,
Message Channel, Event Message, ...) are realised by the exchange model and components; they are listed for completeness.

| Camel EIP | Status | Notes |
|---|---|---|
| Aggregate | ✅ | Correlation, completion size/timeout/interval/predicate/batch consumer/new group, eager check, close keys, discard, optimistic locking, recoverable repository. [Aggregator](ARCHITECTURE.md#aggregator) |
| Bean | ✅ | Instance, type, registry name; method selection and parameter binding. [Bean binding](ARCHITECTURE.md#bean-binding) |
| Change Data Capture | ➖ | Connector concern (Debezium). |
| Channel Adapter, Message Endpoint, Messaging Gateway, Messaging Mapper, Service Activator, Polling Consumer, Event Driven Consumer, Competing Consumers | ✅ | Component SPI, `IProducerTemplate`/`IConsumerTemplate`, `seda` concurrent consumers, `bean`. |
| Message, Message Channel, Message Bus, Message Router, Message Translator, Message Broker, Message Dispatcher, Message Endpoint, Pipes and Filters, Event Message, Request Reply, Return Address, Correlation Identifier, Point to Point, Publish Subscribe | ✅ | Exchange model (`InOnly`/`InOut`), `seda` `multipleConsumers`, `CaravanCorrelationId`. |
| Choice | ✅ | `when`/`otherwise`. A `when`'s `precondition` (Camel 4 choice optimisation) is not supported (YAML rejects it). |
| Circuit Breaker | ✅ | Polly v8 instead of Resilience4j: time-based window, timeouts counted, bulkhead with queue. [Circuit breaker](ARCHITECTURE.md#resilience-circuit-breaker) |
| Claim Check | ✅ | Get/GetAndRemove/Set/Push/Pop, filters. |
| Composed Message Processor, Scatter-Gather | ✅ | `split`/`multicast`/`recipientList` with aggregation. |
| Content Enricher (`enrich`) | ✅ | Plus `variableSend`/`variableReceive`. |
| Content Filter | ✅ | `transform`, `setBody`, `removeHeaders`, claim check filters. |
| Convert Body To | ✅ | |
| Convert Header To | ✅ | |
| Convert Variable To | ✅ | New in this PR. [Variables](ARCHITECTURE.md#variables) |
| Custom Load Balancer | ✅ | |
| Dead Letter Channel | ✅ | [Error handling](ARCHITECTURE.md#error-handling) |
| Delay | ✅ | Non-blocking by default; `callerRunsWhenRejected` has no effect (no executor). |
| Durable Subscriber, Guaranteed Delivery, Transactional Client | ➖ | Broker/connector features; see [transactions](#error-handling-and-transactions). |
| Dynamic Router | ✅ | |
| Enrich / Poll Enrich | ✅ | `AggregateNoMessage` replaces Camel's `aggregate(original, null)`. |
| Failover / Random / Round Robin / Sticky / Topic / Weighted Load Balancer | ✅ | [Routing and flow control](ARCHITECTURE.md#routing-and-flow-control) |
| Fault Tolerance (MicroProfile) | ➖ | Java-specific; the circuit breaker covers it. |
| Filter | ✅ | `statusPropertyName` is fixed (`CaravanFilterMatched`). |
| From | ✅ | Plus `variableReceive` (Camel 4). |
| Idempotent Consumer | ✅ | Eager/non-eager, `completionEager`, `skipDuplicate`, `removeOnFailure`; in-memory repository (durable ones are connectors). |
| Intercept (`intercept`, `interceptFrom`, `interceptSendToEndpoint`) | ✅ | [Interception](ARCHITECTURE.md#interception) |
| Kamelet (`kamelet` EIP) | ✅ | |
| Load Balance | ✅ | |
| Log | ✅ | |
| Loop | ✅ | Count, `doWhile`, `copy`, `breakOnShutdown`. |
| Marshal / Unmarshal | ✅ | Data formats below. |
| Message Expiration | ➖ | Broker feature (JMS TTL). |
| Message History | ✅ | [Observability](ARCHITECTURE.md#observability) |
| Multicast | ✅ | Bounded parallelism (`maxDegreeOfParallelism`) instead of an executor. |
| Normalizer | ✅ | `choice` + `transform`/data type transformers. |
| On Fallback | ✅ | |
| Pipeline | ✅ | |
| Poll | ✅ | Camel 4's `poll`, new in this PR (timeout 20 s, Simple URI, `variableReceive`). |
| Process | ✅ | Lambdas and `IProcessor`, by reference. |
| Process Manager, Routing Slip | ✅ | `routingSlip`. |
| Recipient List | ✅ | |
| Remove Header / Headers / Property / Properties / Variable | ✅ | `removeVariable` new in this PR. |
| Resequence | ✅ | Batch and stream; `comparator` fixed to numeric sequences in YAML. |
| Resilience4j | ✅ | Mapped to Polly (`resilience4jConfiguration` in YAML; half-open and slow-call options ignored). |
| Resume (Camel resume strategies) | ❌ | Offset-resume API for connectors; will come with the first connector needing it. |
| Rollback | ✅ | `markRollbackOnly`, `markRollbackOnlyLast` (scoped to the route, no transactions). |
| Saga | ✅ | In-memory saga service; LRA coordinator would be a connector. [Sagas](ARCHITECTURE.md#sagas) |
| Sample | ✅ | |
| Script | ✅ | Any registered language. |
| Selective Consumer | ✅ | `filter`. |
| Service Call | ➖ | Removed from Camel 4. |
| Set Body / Header / Headers / Property / Variable / Variables | ✅ | `setVariable(s)` new in this PR. |
| Sort | ✅ | |
| Split | ✅ | Strings are not split on commas by default (tokenizer or `delimiter`). [Split](ARCHITECTURE.md#multicast-split-and-recipient-list) |
| Step | ✅ | |
| Stop | ✅ | |
| Threads | 🟡 | New in this PR: bounded pool with Java's executor rules, chained or block form, `Abort`/`CallerRuns`. No `executorService` ref, `keepAliveTime` or thread names (workers are tasks). [Threads](ARCHITECTURE.md#threads-and-poll) |
| Throttle | ✅ | Concurrent requests (Camel 4) and total requests per period (Camel 3). |
| To / ToD | ✅ | Plus `variableSend`/`variableReceive` (`toV`). |
| Tokenizer (LangChain4j) | ➖ | AI connector. |
| Transform | ✅ | Expression, or Camel 4 data types (`toType`/`fromType`), new in this PR. |
| Validate | ✅ | `predicateExceptionFactory` not supported. |
| Wire Tap | ✅ | Plus `variableSend`; the tap always sends `InOnly` (no `pattern`). |
| Do Try / Do Catch / Do Finally, On Exception, On Completion, Throw Exception | ✅ | [Error handling](#error-handling-and-transactions) |
| Policy (`policy` EIP) | 🟡 | Route policies are supported; the `policy` EIP (wrapping a block with a `Policy` such as a transaction or authorization policy) is not. |
| When Skip Send To Endpoint | ✅ | `interceptSendToEndpoint` with `skipSendToOriginalEndpoint` + `onWhen` (the YAML shortcut step is rejected with that hint). |

## Core components

| Camel component | Status | Notes |
|---|---|---|
| `bean` | ✅ | |
| `browse` | ✅ | |
| `class` | ✅ | |
| `controlbus` | ✅ | Route actions and stats, `language` action. |
| `dataformat` | ✅ | Producer only, as Camel. |
| `dataset` / `dataset-test` | ✅ | |
| `direct` | ✅ | `block`/`timeout`, `failIfNoConsumers`. |
| `file` | 🟡 | Most consumer/producer options and read locks; missing: `probeContentType`, `idempotent-changed`/`idempotent-rename` read locks, `bridgeErrorHandler`, `extendedAttributes`, `bufferSize`, ... [File component](ARCHITECTURE.md#file-component) |
| `kamelet` | ✅ | Producer (`to`) and, new in this PR, consumer (`from("kamelet:x")` with `kamelet:sink`). |
| `language` | ✅ | New in this PR: `language:name:script?transform=..&cacheScript=..`, the body as script when there is none. |
| `log` | ✅ | Formatter options (`showAll`, `showHeaders`, ...), `groupSize`. |
| `mock` | ✅ | Expectations, fluent clauses, reply bodies/headers, assert periods. |
| `platform-http` | 🟡 | ASP.NET Core endpoint routing (separate module in Camel too). No engine/port/SSL options (the host decides). [ASP.NET Core](ARCHITECTURE.md#aspnet-core) |
| `ref` | ✅ | |
| `rest` | 🟡 | Consumer (REST DSL transport SPI); no REST producer (`to("rest:...")`). |
| `rest-api` | ✅ | OpenAPI 3 document generated from the REST model (`MapCaravan(o => o.OpenApiPath = ...)`). |
| `saga` | ✅ | |
| `scheduler` | ✅ | |
| `seda` | ✅ | Bounded/unbounded queues, concurrent and multiple consumers, `waitForTaskToComplete`, back-pressure. |
| `stub` | ✅ | |
| `timer` | ✅ | |
| `vm` | ✅ | Process-wide `seda`. |
| `validator` (XSD) | ❌ | Separate module in Camel; `XmlSchemaSet` makes it straightforward. |
| `xslt` / `xslt-saxon` / `xj` | ➖ | Not in camel-core; XML transformation connectors. |
| `properties` (endpoint) | ❌ | `{{placeholders}}` work everywhere; the `properties:key` endpoint indirection does not. |
| `bean-validator`, `jsonpath`, `xpath` components | ❌ | Not core; would come with JSON/XML query languages. |
| `file-watch`, `ref` beans of Spring, `spring-event` | ➖ | Not core / Spring-specific. |

## Languages

| Camel language | Status | Notes |
|---|---|---|
| `simple` | 🟡 | Functions, operators, OGNL, dates, `${variable.x}`/`${variables}`/`${messageTimestamp}` (new in this PR), file functions. Missing: the string/math/collection functions added through Camel 4.x (`replace`, `substring*`, `trim`, `uppercase`, `iif`, `sum`, `split`, `hash`, ...), `jsonpath`/`xpath` functions. [Simple](ARCHITECTURE.md#languages-and-the-simple-language) |
| `constant` | ✅ | |
| `header` | ✅ | |
| `exchangeProperty` | ✅ | |
| `variable` | ✅ | New in this PR. |
| `ref` | ✅ | |
| `tokenize` | ✅ | Token, regex, group, skipFirst; no XML/pair tokenizing. |
| `bean` / `method` | ✅ | No OGNL method chains. |
| `file` | ✅ | The `file:` functions are part of Simple (as in Camel 4). |
| C# lambdas (`Expr`, `Pred`) | ✅ | Caravan's equivalent of Camel's Java DSL expressions. |
| `csimple` (compiled simple) | ➖ | C# lambdas are the compiled option. |
| `jq` / `jsonpath` / `xpath` / `xquery` | ❌ | Not in camel-core modules but commonly used; planned with JSON/XML connectors. |
| `groovy`, `js`, `datasonnet`, `joor`, `java` | ➖ | Script engines of the JVM; not core. |
| `hl7terser`, `ognl`, `mvel`, `spel` | ➖ | Not core; no plans. |

## Data formats

| Camel data format | Status | Notes |
|---|---|---|
| `json` (Jackson) | ✅ | System.Text.Json, camelCase by default. |
| `jacksonXml` / `jaxb` (`xml`) | ✅ | `XmlSerializer`; untyped → `XDocument`. |
| `csv` | ✅ | RFC 4180 options, maps or lists. |
| `base64` | ✅ | |
| `gzipDeflater` | ✅ | |
| `zipDeflater` | ✅ | |
| `string` | ✅ | |
| `zipFile` | ❌ | |
| `serialization` (Java) | ➖ | BinaryFormatter is obsolete and unsafe. |
| `avro`, `protobuf`, `bindy`, `fhir`, `hl7`, ... | ➖ | Not core: connector packages. |

## DSLs

| Camel DSL | Status | Notes |
|---|---|---|
| Java DSL | ✅ | C# DSL: nested lambdas instead of `.end()`, typed overloads (`Filter((Order o) => ...)`). |
| YAML DSL | ✅ | Camel's shapes, strict located errors, JSON schema, hot reload; every Caravan EIP has a step. [YAML DSL](ARCHITECTURE.md#yaml-dsl) |
| Rest DSL | ✅ | [REST DSL](ARCHITECTURE.md#rest-dsl) |
| Route dump (`camel dump`, `DumpRoutesStrategy`) | 🟡 | `IManagementService.DumpRouteAsText` gives an indented text model; there is no YAML/XML dump that round-trips (the YAML catalog builds definitions but does not read them back). |
| XML DSL (`camel-xml-io`) | ❌ | Not planned for now: YAML covers declarative routes. |

## Error handling and transactions

| Camel feature | Status | Notes |
|---|---|---|
| Default error handler | ✅ | |
| Dead letter channel (`useOriginalMessage/Body`, `deadLetterHandleNewException`) | ✅ | |
| No error handler | ✅ | |
| Redelivery policy (delay, back-off, collision avoidance, `delayPattern`, `retryWhile`, `retryAttemptedLogLevel`, ...) | ✅ | Delays are always non-blocking. |
| `onRedelivery`, `onExceptionOccurred`, `onPrepareFailure` | ✅ | |
| `onException` (route/global scope, `handled`, `continued`, `onWhen`, type matching along the cause chain) | ✅ | |
| `doTry` / `doCatch` / `doFinally` | ✅ | |
| `onCompletion` (`onCompleteOnly`, `onFailureOnly`, `onWhen`, mode, parallel) | ✅ | |
| Unit of work, `Synchronization`, handover | ✅ | |
| `throwException`, `rollback` | ✅ | |
| Route configurations (`routeConfiguration`) | ✅ | |
| `transacted`, `policy` with transaction policies, JTA/Spring transaction error handlers | ➖ | There is no XA/ambient distributed transaction model in .NET to plug into. Use the **outbox pattern** (write the message and the business change in one database transaction, a route publishes the outbox), **idempotent consumers** (`idempotentConsumer` with a durable `IIdempotentRepository`) and **sagas** for compensation. |
| `TransactionErrorHandler` | ➖ | See above. |

## Routes: lifecycle, policies and configuration

| Camel feature | Status | Notes |
|---|---|---|
| Route ids, groups, descriptions, properties | ✅ | |
| `autoStartup`, `startupOrder` | ✅ | |
| Route controller: start/stop/suspend/resume/remove | ✅ | |
| Supervising route controller | ✅ | |
| Graceful shutdown (`shutdownRoute`, `shutdownRunningTask`, timeout, forced shutdown) | ✅ | [Consumers](ARCHITECTURE.md#consumers-lifecycle-suspension-and-shutdown) |
| Route policies (`RoutePolicy`, `routePolicy`/`routePolicyRef`, `RoutePolicyFactory`) | ✅ | New in this PR. [Route policies](ARCHITECTURE.md#route-policies) |
| `ThrottlingInflightRoutePolicy` | ✅ | New in this PR. Context scope is checked when the route's own exchanges complete. |
| `ThrottlingExceptionRoutePolicy` | ✅ | New in this PR (threshold, window, half-open delay and handler, `keepOpen`, throttled exceptions). |
| `SimpleScheduledRoutePolicy` / `CronScheduledRoutePolicy` | ➖ | Not core (camel-quartz/camel-cron); write a `RoutePolicySupport` with a timer, or use `controlbus`. |
| Route `precondition` | ✅ | New in this PR (also on route configurations). |
| `nodePrefixId`, templated route `prefixId` | ✅ | New in this PR. |
| Route filters (`routeFilterIncludePattern`/`ExcludePattern`) | ✅ | |
| Route reload (watch YAML files) | ✅ | `RoutesLoader` with `Watch`. Camel can also reload Java routes via JBang; C# routes are compiled. |
| Stream caching (per route / context, spooling) | ✅ | [Stream caching](ARCHITECTURE.md#stream-caching) |
| `streamCache`, `messageHistory`, `trace`, `logMask`, `delayer` route options | ✅ | Stream caching per route; history, tracing are context options; `logMask`/`delayer` are rejected in YAML. |
| Interceptors, `interceptFrom`, `interceptSendToEndpoint` | ✅ | |
| Event notifier (context, route, exchange events) | ✅ | |
| Inflight repository | ✅ | |

## Route templates and Kamelets

| Camel feature | Status | Notes |
|---|---|---|
| Route templates, parameters, defaults, optional parameters | ✅ | [Route templates](ARCHITECTURE.md#route-templates) |
| Template beans (local beans) | ✅ | Factories or types, no scripts. |
| Templated routes (`templatedRoute`), parameter sources (configuration) | ✅ | |
| `kamelet` producer (`to("kamelet:x")`) | ✅ | |
| `kamelet` consumer (`from("kamelet:x")`) | ✅ | New in this PR. |
| Template bean scripts (`groovy`, ...) | 🟡 | Factories/types only. |
| Kamelet YAML specs (`apiVersion: camel.apache.org/v1`, `kind: Kamelet`) and the Kamelet catalog | ❌ | Templates are defined with `routeTemplate` (C# or YAML); the Kamelet CRD format is not read. |

## Data types: transformers and validators

| Camel feature | Status | Notes |
|---|---|---|
| Route `inputType` / `outputType` (with `validate`) | ✅ | New in this PR. [Data types](ARCHITECTURE.md#data-types-transformers-and-validators) |
| `transform` with `toType`/`fromType` | ✅ | New in this PR. |
| Data format transformer | ✅ | |
| Endpoint transformer | ✅ | |
| Custom transformer (class / bean) | ✅ | `ITransformer`, lambdas (`DelegateTransformer`). |
| Predicate / endpoint / custom validators | ✅ | |
| `loadTransformer` (annotation-scanned `@DataTypeTransformer`) and named built-in data types (`application-cloudevents`, ...) | ❌ | Transformers are registered explicitly. |

## Variables

| Camel 4 feature | Status | Notes |
|---|---|---|
| Exchange variables | ✅ | New in this PR. [Variables](ARCHITECTURE.md#variables) |
| Global variables (`global:`) | ✅ | |
| Route variables (`route:`, `route:id:`) | ✅ | |
| Custom variable repositories | ✅ | |
| `setVariable`, `setVariables`, `removeVariable`, `convertVariableTo` | ✅ | |
| `${variable.x}`, `${variables}`, `${variableAs(...)}`, `variable` language | ✅ | |
| `variableSend` / `variableReceive` (`to`, `toD`, `enrich`, `pollEnrich`, `poll`, `from`) | ✅ | Received headers are stored as `name.header.key` variables. |

## Properties and configuration

| Camel feature | Status | Notes |
|---|---|---|
| `{{key}}`, defaults, nested, `env:`, `sys:`, optional `{{?key}}` | ✅ | [Properties](ARCHITECTURE.md#properties) |
| Property sources (files, `IConfiguration`), override/initial properties | ✅ | `IConfiguration` replaces `.properties` files. |
| Placeholders in URIs, Simple, ids, options | ✅ | |
| Encrypted properties (Jasypt) | ❌ | Use .NET secret stores through `IConfiguration`. |
| `properties:` Simple function | ✅ | |
| Camel main / Spring Boot configuration | ✅ | `Caravan.Hosting` options bound from `Caravan` configuration. |
| Global options (`autoStartup`, stream caching, `allowUseOriginalMessage`, uuid generator, ...) | ✅ | [Global configuration](ARCHITECTURE.md#global-configuration) |
| Startup conditions (Camel 4.3: wait for env/file/class before starting) | 🟡 | The Generic Host's ordering and health checks cover it; there is no built-in condition list. |
| Registry, `#bean` references, DI | ✅ | Backed by `IServiceProvider` when hosted. |

## Testing

| `camel-test-junit5` feature | Status | Notes |
|---|---|---|
| `CamelTestSupport` | ✅ | `CaravanTestSupport` (test-framework agnostic). [Testing toolkit](ARCHITECTURE.md#testing-toolkit) |
| AdviceWith (weave by id/uri/type/toString, replace/remove/before/after, `replaceFromWith`, add first/last) | ✅ | |
| Mocking endpoints (`mockEndpoints`, `mockEndpointsAndSkip`) | ✅ | |
| `MockEndpoint` expectations | ✅ | |
| `NotifyBuilder` | ✅ | |
| `dataset` testing | ✅ | |
| Fake clock | ✅ | `TimeProvider` everywhere (better than Camel: deterministic timers). |
| `ProducerTemplate` / `ConsumerTemplate` | ✅ | |
| Route coverage / `@EnableRouteCoverage` | 🟡 | Per-node statistics of the management API give the counts; no coverage report. |
| `camel-test-infra` (containers for brokers) | ➖ | Testcontainers for .NET is the equivalent; connector-specific. |
| Debugger hooks in tests (`debugBefore`/`debugAfter`) | ❌ | `UseAdviceWith` exists; there are no step-debug callbacks. |

## Management, observability and health

| Camel feature | Status | Notes |
|---|---|---|
| JMX MBeans | ➖ | Replaced by the `IManagementService` API and its HTTP endpoints. [Management](ARCHITECTURE.md#management) |
| Route / processor / context statistics, statistics levels | ✅ | |
| Management operations (start/stop/suspend/resume, dump route, endpoints, components, inflight) | ✅ | |
| Message history | ✅ | |
| Tracer, backlog tracer | ✅ | |
| OpenTelemetry tracing and metrics (`camel-opentelemetry`, `camel-micrometer`) | ✅ | Built in; W3C propagation. [Observability](ARCHITECTURE.md#observability) |
| Health checks (context, routes, consumers, components, registry) | ✅ | [Health checks](ARCHITECTURE.md#health-checks) |
| Health/management over HTTP (Camel main console endpoints) | ✅ | `MapCaravan`. |
| MDC logging | 🟡 | Spans carry route/exchange ids; there is no `ILogger` scope with `camel.exchangeId` etc. yet. |
| Developer console (`camel-console`) | ❌ | |
| Debugger (`BacklogDebugger`) | ❌ | |
| Startup recorder (`StartupStepRecorder`, JFR) | ❌ | Context/route events and the hosted service's start summary only. |

## Runtime: threading, shutdown and type converters

| Camel feature | Status | Notes |
|---|---|---|
| Asynchronous routing engine | ✅ | `ValueTask`-based processors. |
| `ExecutorServiceManager`, thread pool profiles | ➖ | The .NET thread pool; bounded parallelism per EIP (`maxDegreeOfParallelism`, `threads`). |
| `threads` EIP | 🟡 | See EIPs. |
| Graceful shutdown strategy | ✅ | |
| Type converters (built-in, custom, fallback) | ✅ | `TypeConverterRegistry.AddConverter`. |
| `@Converter` classpath scanning | ➖ | Converters are registered in code. |
| UUID generators | ✅ | |
| Stream caching | ✅ | |
| Clock / `TimeProvider` | ✅ | |
| Camel context lifecycle (start/stop/suspend/resume, services) | ✅ | |
| Route templates loaded from the classpath | ➖ | Loaded from assemblies or YAML files. |
| Generic Host integration (Camel main / Spring Boot) | ✅ | `Caravan.Hosting`. |
| Virtual threads | ➖ | Async I/O covers the use case. |
