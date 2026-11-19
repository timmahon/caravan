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
| `ErrorHandlerDefinition` | Route error handler configuration: default, dead letter channel, none (see "Error handling") | `ErrorHandlerFactory` |
| `RedeliveryErrorHandler` / `RedeliveryPolicy` | Per-node redelivery, `onException`, dead letter channel | `RedeliveryErrorHandler` / `RedeliveryPolicy` |
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
| `IAggregationStrategy` / `AggregationStrategies` | Combine exchanges (multicast, split, recipient list, aggregator) | `AggregationStrategy` / `AggregationStrategies` |
| `MulticastProcessor` | Fan-out engine of `multicast`, `split` (`SplitProcessor`) and `recipientList` (`RecipientListProcessor`) | `MulticastProcessor` |
| `AggregateProcessor` / `IAggregationRepository` | The `aggregate` EIP and the store of its groups (`MemoryAggregationRepository` by default) | `AggregateProcessor` / `AggregationRepository` |
| `BatchResequencerProcessor` / `StreamResequencerProcessor` | The `resequence` EIP (batch and stream) | `Resequencer` / `StreamResequencer` |
| `ProducerCache` | LRU cache of started senders per endpoint, for dynamic endpoints (`toD`, `wireTap`, `recipientList`) | `ProducerCache` |

### Error semantics

- Processors **may throw**. `Channel` catches the exception and sets `exchange.Exception`.
- `Pipeline` stops when `exchange.IsFailed`, `exchange.IsRouteStop`, `exchange.IsRollbackOnly`/`IsRollbackOnlyLast` or
  `exchange.ErrorHandlerHandled` (a failure handled by an error handler ends routing, as in Camel).
- `RouteProcessor` creates the unit of work only if the exchange has none (so `direct` calls between routes share one).
  Callbacks added with `Exchange.AddOnCompletion` before a unit of work exists are taken over by it. When an exchange is
  handed to another task (e.g. `seda`), its synchronizations are handed over to the copy unless
  `ISynchronization.AllowHandover` is false.
- `IProducerTemplate.Send*Body*` / `Request*` throw `CaravanExecutionException` when the exchange failed;
  `SendAsync(uri, exchange)` returns the exchange with `Exception` set instead.

### Error handling

Camel's error handling model, built on `IErrorHandlerFactory` (see "Extension points"):

- **Configuration.** An `ErrorHandlerDefinition` (`Caravan.Model`) is chosen per route: `RouteDefinition.ErrorHandler`
  (`.ErrorHandler(...)` on a route) → the route builder's (`RouteBuilder.ErrorHandler(...)`) → `ICaravanContext.ErrorHandler`
  → `DefaultErrorHandlerDefinition`. `CaravanContext.CreateRoute` calls `CreateFactory(routeContext)` and sets
  `RouteContext.ErrorHandlerFactory` *before* the `RouteContextConfigurers` run (they may still replace it). Built-ins:
  `DefaultErrorHandler()` (no redelivery, logs at ERROR with the exchange, failure propagates), `DeadLetterChannel(uri)`
  (sends exhausted failures to `uri` and marks them handled) and `NoErrorHandler()` (returns no factory: nodes are not
  wrapped, `onException` does not apply). Options are extension methods: `RedeliveryDsl` (redelivery policy and logging, shared
  with `onException`) and `ErrorHandlerDsl` (`OnRedelivery`, `OnExceptionOccurred`, `OnPrepareFailure`, `RetryWhile`,
  `UseOriginalMessage`/`UseOriginalBody`). Using the original message turns `AllowUseOriginalMessage` on automatically.
- **Per node.** `RedeliveryErrorHandlerFactory` wraps every node in a `RedeliveryErrorHandler`, so a redelivery retries only the
  failing node, with the message restored as it was before the node's first attempt and the `CaravanRedelivered`,
  `CaravanRedeliveryCounter` and `CaravanRedeliveryMaxCounter` headers set. Delays (`RedeliveryPolicy`: fixed, exponential
  back-off, collision avoidance, `delayPattern`) are awaited on `context.TimeProvider`. Nodes of `doTry`/`doCatch`/`doFinally`
  and of `onException` blocks are **not** wrapped: they handle errors themselves.
- **Exhaustion.** When redelivery is exhausted the failure processor runs on the same exchange with `Exception` cleared and
  `CaravanExceptionCaught`, `CaravanFailureEndpoint`, `CaravanFailureRouteId` and `CaravanFailureTimestamp` set: the matching
  `onException` block, else the dead letter endpoint. Then the failure is *handled* (`ErrorHandlerHandled`: routing stops, the
  caller sees success), *continued* (routing goes on with the next node) or *propagated* (exception restored,
  `IsRedeliveryExhausted` set). An exhausted exchange passes through the error handlers of enclosing nodes and calling routes
  untouched, so a failure is handled once, by the innermost error handler; a sub-route with `NoErrorHandler()` leaves it to the
  caller. A new exception thrown by the failure processor fails the exchange and is not handled again (the dead letter channel
  handles it when `deadLetterHandleNewException`, the default). Events: `ExchangeRedelivery`, `ExchangeFailureHandled`.
- **onException.** `OnExceptionDefinition`s live in `RouteDefinition.OnExceptions`: route-scoped ones (`.OnException<T>(...)` on a
  route) and the route builder's (`RouteBuilder.OnException<T>(...)`, added to each of its routes by `RouteBuilder.Build`).
  `ExceptionPolicyStrategy` picks one like Camel: route-scoped before global, cause chain from the innermost exception
  outwards, exact type wins at once, else the closest type in the hierarchy; `OnWhen` filters. A clause's redelivery options
  override the error handler's; its steps are its own block (`OnExceptionBuilder` is a `RouteSteps`).
- **doTry.** `TryProcessor` runs the try block, the first catching `doCatch` (cause chain considered) and the `doFinally`; a pending
  exception is restored after `doFinally`. The route's error handler and `onException` do not apply inside `doTry`.
- **onCompletion.** `RouteDefinition.OnCompletions` (route-scoped ones replace the builder's). `OnCompletionProcessor` wraps the
  route's pipeline: it registers one `ISynchronization` per block on the unit of work (once per exchange, even across routes).
  `AfterConsumer` (default) blocks run when the unit of work is done, on a correlated `InOnly` copy; `BeforeConsumer` blocks run
  when the registering route finishes, on the exchange itself (they can change the reply); `ParallelProcessing` runs a copy in
  the background. Failures in blocks are logged and ignored.
- **Rollback.** `Rollback(msg)` sets `IsRollbackOnly` and throws `RollbackExchangeException` (never redelivered);
  `MarkRollbackOnly()` only marks the exchange (routing stops, unit of work fails); `MarkRollbackOnlyLast()` stops the current
  route only: `RouteProcessor` clears the flag when the exchange returns to a calling route.

Differences from Camel: redelivery delays are always non-blocking (`asyncDelayedRedelivery` is accepted but has no effect);
the dead letter endpoint and `onException` blocks see the redelivery headers, which are removed once the failure is handled
or continued; `AfterConsumer` `onCompletion` blocks run on a copy because the unit of work completes before a `direct` caller
gets the reply; `markRollbackOnlyLast` scopes to the current route since there are no transactions.

### Multicast, split and recipient list

`multicast`, `split` and `recipientList` share one engine, `MulticastProcessor` (options: `MulticastSettings`; definitions derive
from `FanOutDefinition`, DSL options are named parameters such as `.Split(Expr.Tokenize(","), s => ..., parallelProcessing: true)`).
`SplitProcessor` and `RecipientListProcessor` only supply the branches (`CreateBranchesAsync`) and the branch properties.

- **Branches.** Each branch processes a correlated copy (`CreateCorrelatedCopy`) of the input exchange. Multicast branches are the steps
  of its block (each built with `CreateNodeProcessor`), a split runs its block (`CreateBlockProcessor`) per part, a recipient list sends
  through a `ProducerCache`, each send wrapped with the route's `IErrorHandlerFactory`. So error handling applies *per branch*: a
  failing step is redelivered and handled (dead letter channel, `onException` with `handled`) inside its branch, and the EIP continues.
  `stop` in a branch only ends that branch. Properties: `CaravanMulticastIndex`/`CaravanMulticastComplete` (multicast, recipient list,
  plus `CaravanRecipientListEndpoint`), `CaravanSplitIndex`/`CaravanSplitSize`/`CaravanSplitComplete` (split).
- **Unit of work.** Each branch gets its own `DefaultUnitOfWork`, completed when the branch is done (before aggregation). With
  `shareUnitOfWork` the branch uses the input exchange's unit of work instead (completion callbacks run with the parent,
  `useOriginalMessage` sees the parent's message) and is flagged so its error handler, once redelivery is exhausted, does not run its
  failure processor; the failure is set on the input exchange with redelivery suppressed, so the parent's error handler goes straight to
  its failure processor (Camel's `SplitSubUnitOfWorkTest` behaviour).
- **Aggregation.** `IAggregationStrategy.Aggregate(old, new)` (and the input-aware overload) is called for every branch, failed ones
  included, in branch order; the final result gets `OnCompletion` and is copied into the input exchange (message replaced, properties
  merged, exception and failure flags taken over). Failures propagate only if the strategy keeps them: the defaults do — `multicast`
  and `recipientList` use `UseLatestAggregationStrategy`, `split` uses `UseOriginalAggregationStrategy` with exception propagation (the
  input exchange continues unchanged, failing if a part failed). The input exchange keeps the failed branch's `IsRedeliveryExhausted`,
  so enclosing error handlers do not handle the failure a second time. `stopOnException` stops at the first failed branch (exception,
  rollback, or a failure an error handler handled) and fails the input exchange with `CaravanExchangeException("Multicast processing
  failed for number N")` (or takes the handled branch as the result). The strategy API also carries the aggregator's hooks
  (`OnTimeout`, `CanPreComplete`/`PreComplete`, see below). Built-ins (`Caravan.Aggregation`, factories on `AggregationStrategies`): UseLatest,
  UseOriginal, GroupedExchange, GroupedBody, GroupedMessage, String, `From(lambda)`, `Body<TItem, TResult>(fold)`, `Flexible<T>()`.
  `aggregationStrategyRef` looks a strategy up in the registry when the route is built. Keep per-aggregation state on the exchanges
  (the grouped strategies use the `CaravanGroupedExchange` property), never in strategy fields: one instance serves all exchanges.
- **Parallel processing.** Branches run on the thread pool, bounded by a `SemaphoreSlim` of `maxDegreeOfParallelism` slots (default
  10, Camel's default pool size); branches are created lazily as slots free up, so a streaming split of a huge input never holds more than
  that many parts in flight. Aggregation is serialized under a lock and done in branch order (early results are buffered) unless
  `streaming` (completion order); `parallelAggregate` (streaming only) lets strategy calls run concurrently. A slot is released only after
  the branch's result was aggregated, so `stopOnException` deterministically prevents further dispatch. The processor waits for every
  started branch, except after `timeout` (parallel only, measured on `context.TimeProvider`): unfinished branches get `OnTimeout`, are
  cancelled through their `Exchange.CancellationToken` and ignored; they are tracked and the processor's stop waits for them.
- **Splitting values.** A split expression may return an `IEnumerable` (not a string), array, `IAsyncEnumerable<T>`, a `Message` per
  part, null (no parts) or a single value. Non-streaming splits read all parts first (`CaravanSplitSize` on every part); streaming splits
  read one part ahead of processing (to know the last one) and set the size on the last part only. `Expr.Tokenize(token, regex, group,
  skipFirst)` / `Expr.Lines()` (and `.Tokenize(...)`/`.TokenizeLines()` on any expression) read strings, readers, streams or bytes
  lazily in chunks, synchronously or asynchronously.
- **Cancellation.** When the input exchange's `CancellationToken` is cancelled no further branch starts, running branches are awaited,
  and the input exchange fails with an `OperationCanceledException`.

Differences from Camel: a string is not split on commas by default (only with the `delimiter` option or a tokenizer); `stop` in a
branch never stops the parent; with `shareUnitOfWork` a branch failure is always left to the parent's error handler (sub-level
`onException` handling is skipped); the timeout is ignored without parallel processing (as Camel) and cancels unfinished branches
(Camel lets them run); there is no executor service option, only `maxDegreeOfParallelism`.

### Aggregator

`.Aggregate(correlation, strategy, a => a.CompletionSize(3).CompletionTimeout(...), then => then.To(...))`: `AggregateDefinition`
(options are plain properties, `AggregateBuilder` sets them; `AggregateSettings` is the processor's copy) and `AggregateProcessor`, a port of
Camel's `AggregateProcessor`.

- **Groups.** The correlation expression's value, as a string, is the key; a null or blank key fails the exchange
  (`CaravanExchangeException("Invalid correlation key")`) unless `IgnoreInvalidCorrelationKeys`. Each incoming exchange is copied
  (`CreateCorrelatedCopy`), the group is read from the repository, the copy is aggregated into it (`Aggregate(old, new)`), the result gets
  `CaravanAggregatedSize` and is stored back unless the group completed. The incoming exchange then continues unchanged with the steps after
  the `aggregate`. Aggregation is serialized by one lock per processor (Camel's default), or runs without it with `OptimisticLocking`: the
  repository (an `IOptimisticLockingAggregationRepository`) detects conflicting updates (`OptimisticLockingException`) and the whole
  aggregation is retried on a fresh copy per `OptimisticLockRetryPolicy` (delays on the context's clock), failing the exchange with
  "Exhausted optimistic locking retry attempts" in the end.
- **Completion.** At least one condition is required (else the route fails to be created). Checked after aggregating, on the aggregated exchange,
  or with `EagerCheckCompletion` on the incoming one (with `CaravanAggregatedSize` set to the size the group will have), in Camel's order:
  `CompletionFromBatchConsumer` (completes every group of the batch once `CaravanBatchSize` exchanges arrived or one has `CaravanBatchComplete`),
  the `CaravanAggregationCompleteCurrentGroup` property set by the strategy, `CompletionPredicate` (or the strategy itself when it is an
  `IPredicate`), `CompletionSize` (an expression takes precedence when positive). `CompletionTimeout` (fixed or per exchange) is an inactivity
  timeout per group, restarted by every exchange and checked every `CompletionTimeoutCheckerInterval` (1 s) by a background loop;
  `CompletionInterval` completes all groups periodically (not combinable with a fixed timeout, as in Camel). A strategy with `CanPreComplete`
  completes the group *before* the new exchange when `PreComplete(old, new)` says so; the new exchange starts the next group.
  `CompletionOnNewCorrelationGroup`, the `CaravanAggregationCompleteAllGroups` header (exchange not aggregated) or property (set by the strategy)
  and the `...Inclusive` header force the completion of other groups. `CaravanAggregatedCompletedBy` is `size`, `timeout`, `interval`,
  `predicate`, `consumer`, `force` or `strategy` (`AggregationCompletedBy`); `CaravanAggregatedCorrelationKey` and, for timeouts,
  `CaravanAggregatedTimeout` are set too.
- **After completion.** The group is removed from the repository and its timeout, its key is closed with `CloseCorrelationKeyOnCompletion(capacity)`
  (later exchanges fail with `ClosedCorrelationKeyException`; oldest keys forgotten beyond the capacity, 0 = unbounded), `OnTimeout(exchange, -1, -1,
  timeout)` is called for timeouts, then the group is discarded (`DiscardOnCompletionTimeout`, or `DiscardOnAggregationFailure` when the strategy
  threw: the incoming exchange then succeeds) or sent. Sending calls the strategy's `OnCompletion(exchange, input)` and processes the aggregated
  exchange through the nested block as a *new* exchange, detached from the incoming ones (`DetachedExchangeSender`: own unit of work, tracked in
  the inflight repository under the route id, remaining failures logged). Without `ParallelProcessing` it is sent on the completing thread (the
  incoming exchange's, before it continues; the timeout checker's; the caller of a forced completion); with it on the thread pool, at most
  `MaxDegreeOfParallelism` (10) at a time. A successful send confirms the exchange id in the repository.
- **Control and shutdown.** `IAggregateController` (`DefaultAggregateController`) forces the completion or discarding of one group or all.
  `AggregateProcessor` is a route service implementing `IShutdownAware`: when its route stops, the shutdown strategy (after the consumer stopped and
  inflight exchanges completed) calls `PrepareShutdown`, which completes all groups with `ForceCompletionOnStop` (not on suspend), and waits until
  `PendingExchangesSize` (exchanges being sent, plus the stored groups with `CompleteAllOnStop`) is zero. Stopping the processor stops its
  background loops, waits for sends and stops the repository (the memory one then forgets its groups, as in Camel). On start, timeouts are
  restored for groups found in the repository.

**Repository SPI** (`Caravan.Spi`). `IAggregationRepository`: `AddAsync`/`GetAsync`/`RemoveAsync`/`ConfirmAsync`/`GetKeysAsync`, asynchronous so a
durable store (SQL, Redis) fits. Repositories store **copies** (or serialized forms) and return copies, keeping exchange ids;
`RemoveAsync` receives the completed exchange. Repositories that are `IService`s are started and stopped with the processor.
`IOptimisticLockingAggregationRepository` adds a conditional `AddAsync(key, old, new)`; it and `RemoveAsync` throw `OptimisticLockingException`
when the stored group is not the version read (versions travel in `CaravanAggregationRepositoryVersion` and must never be reused for a key).
`IRecoverableAggregationRepository` keeps completed exchanges until confirmed: with `UseRecovery` the processor scans every `RecoveryInterval`,
resubmits unconfirmed exchanges it is not sending (`CaravanRedelivered`, `CaravanRedeliveryCounter`, `CaravanRedeliveryMaxCounter` headers) and
after `MaximumRedeliveries` sends them to `DeadLetterUri` (required then) and confirms them. `MemoryAggregationRepository` is the default
(optimistic locking optional; with it the `CaravanGroupedExchange` list is copied too, so a failed attempt leaves the stored group unchanged).

Differences from Camel: the batch consumer properties are read from the incoming exchange; in pre-completion mode the other conditions are still
checked after aggregating (Camel only checks `preComplete` and timeouts); `ForceCompletionOnStop` does not apply to suspension; there are no
executor service options; a recoverable repository receives the completed exchange rather than the group as last read.

### Resequencer

`.Resequence(expression, r => r.Batch(...) | r.Stream(...), then => ...)` (`ResequenceDefinition`, configs `BatchResequencerConfig` /
`StreamResequencerConfig`; the two-argument overload is a default batch resequencer). Incoming exchanges are copied and continue at once; the
copies are sent through the nested block one at a time, in order, by a single background task (`DetachedExchangeSender` as above). Both
processors are `IShutdownAware` route services: a graceful stop emits what they hold before they stop.

- **Batch** (`BatchResequencerProcessor`, Camel's `Resequencer`): a batch is emitted, sorted by the expression with type coercion
  (`ObjectHelper.TypeCoerceCompare`), once it holds `BatchSize` (100) exchanges or `BatchTimeout` (1 s, an `ITimer` on the context's clock) after
  its first exchange; `AllowDuplicates` (else only the first of equal keys is kept), `Reverse`, `IgnoreInvalidExchanges` (else a null or failing
  key fails the exchange). Difference from Camel: the timeout is not restarted by each arrival.
- **Stream** (`StreamResequencerProcessor`, Camel's `StreamResequencer`/`ResequencerEngine`): `long` sequence numbers are delivered as soon as
  they follow the last delivered one; an exchange with a gap before it (neither the successor of the last delivered nor with its predecessor
  waiting) gets a `Timeout` (1 s) timer, cancelled when the predecessor arrives, after which the gap is given up on. As in Camel the first
  exchange waits for its timeout. Duplicates waiting are dropped; `RejectOld` fails exchanges older than the last delivered one with
  `MessageRejectedException`; senders wait while `Capacity` (1000) exchanges are held; `DeliveryAttemptInterval` bounds how long delivery
  sleeps without a signal. Difference from Camel: held exchanges are delivered on stop instead of dropped.

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
  are built, after the route's configured error handler factory is set (e.g. to replace `RouteContext.ErrorHandlerFactory`).
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
   EIPs that send to endpoints computed per exchange use a `ProducerCache`; EIPs that fan out derive from `MulticastProcessor`.
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
- Route services (processors) may implement `IShutdownAware` too (Camel's `ShutdownPrepared`): when routes stop (not suspend), once consumers
  stopped and inflight exchanges completed, they get `PrepareShutdown(..., forced: false)` and the shutdown waits until their
  `PendingExchangesSize` is zero (aggregator, resequencers).
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
  `Caravan.Processors`, `Caravan.Support`, `Caravan.Impl`, `Caravan.Aggregation` (built-in aggregation strategies),
  `Caravan.Components.<Name>`.
- Tests: xUnit, one test class per feature, descriptive `Snake_case_names`. No sleeps where a mock
  expectation or `TimeProvider` can be used instead; keep the suite fast.
