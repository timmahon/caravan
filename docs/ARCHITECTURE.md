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
| `ProducerCache` | LRU cache of started senders per endpoint, for dynamic endpoints (`toD`, `wireTap`, `recipientList`, `routingSlip`, `dynamicRouter`, `enrich`) | `ProducerCache` |
| `ConsumerCache` | LRU cache of started polling consumers per endpoint (`pollEnrich`) | `ConsumerCache` |
| `IIdempotentRepository` / `MemoryIdempotentRepository` | Processed message ids of the `idempotentConsumer` EIP | `IdempotentRepository` / `MemoryIdempotentRepository` |
| `ILoadBalancer` / `Caravan.LoadBalancing` | Chooses the outputs of the `loadBalance` EIP | `LoadBalancer` |
| `IClaimCheckRepository` / `MemoryClaimCheckRepository` | Per-exchange store of the `claimCheck` EIP | `ClaimCheckRepository` |
| `CircuitBreakerProcessor` / `ICircuitBreakerStateRegistry` | The `circuitBreaker` EIP (Polly v8) and its circuits by node id | `Resilience4jProcessor` / JMX |
| `ISagaService` / `ISagaCoordinator` / `InMemorySagaService` | Saga SPI of the `saga` EIP and `saga` component, in-memory default | `CamelSagaService` / `CamelSagaCoordinator` / `InMemorySagaService` |
| `InterceptDefinition` / `InterceptFromDefinition` / `InterceptSendToEndpointDefinition` | `intercept`, `interceptFrom`, `interceptSendToEndpoint` (see "Interception") | same |
| `ISendToEndpointInterceptor` / `SendToEndpointInterceptors` | Intercept every send to matching endpoints (producer decorator), used by `interceptSendToEndpoint` and endpoint mocking | `InterceptSendToEndpoint` / `EndpointStrategy` |
| `Caravan.Testing`: `AdviceWith`, `NotifyBuilder`, `CaravanTestSupport` | Test kit (see "Testing toolkit") | `camel-test`: `AdviceWith`, `NotifyBuilder`, `CamelTestSupport` |
| `ILanguage` / `LanguageRegistry` / `ICaravanContext.ResolveLanguage` | Expression languages by name (see "Languages and the Simple language") | `Language` / `CamelContext.resolveLanguage` |
| `SimpleLanguage`, `Expr.Simple`, `Pred.Simple`, `Lang.Simple` | The Simple language | `SimpleLanguage`, `simple()` |
| `IPropertiesComponent` / `DefaultPropertiesComponent` | `{{placeholder}}` resolution (see "Properties") | `PropertiesComponent` |
| `IDataFormat` / `DataFormatRegistry` / `ICaravanContext.ResolveDataFormat` | `marshal`/`unmarshal` and the `dataformat` component (see "Data formats") | `DataFormat` / `CamelContext.resolveDataFormat` |
| `StreamCachingStrategy` / `IStreamCache` | Re-readable stream bodies (see "Stream caching") | `StreamCachingStrategy` / `StreamCache` |
| `BeanProcessor` / `BeanExpression` | The `bean` EIP, `bean`/`class` components, `bean`/`method` language (see "Bean binding") | `BeanProcessor` / `BeanExpression` |
| `RouteTemplateDefinition` / `RouteTemplateContext` / `TemplatedRouteBuilder` | Route templates and the routes created from them (see "Route templates") | `RouteTemplateDefinition` / `RouteTemplateContext` / `TemplatedRouteBuilder` |
| `LocalBeanRegistry` | Beans local to one route (template beans), consulted before the context registry | local bean repository |
| `IRouteTemplateParameterSource` / `ConfigurationRouteTemplateParameterSource` | Routes created from templates at startup, e.g. from `IConfiguration` | `RouteTemplateParameterSource` |
| `KameletComponent` | `kamelet:` calls a template as a reusable route snippet | `kamelet` component |
| `RouteConfigurationDefinition` / `RouteConfigurationBuilder` | Shared error handling and interceptors selected by routes (see "Route configuration") | `RouteConfigurationDefinition` / `RouteConfigurationBuilder` |
| `IUuidGenerator` | Exchange, message and saga ids (see "Global configuration") | `UuidGenerator` |

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
  back-off, collision avoidance, `delayPattern`) are awaited on `context.TimeProvider`. Nodes of `doTry`/`doCatch`/`doFinally`,
  of `onException` blocks and inside a `circuitBreaker` (block and fallback, unless `inheritErrorHandler`) are **not** wrapped: they
  handle errors themselves.
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

### Routing and flow control

Definitions, processors and DSL follow "Adding an EIP"; the EIPs below keep Camel's semantics except where noted.

- **routingSlip / dynamicRouter** (`RoutingSlipProcessor`, `DynamicRouterProcessor`). The slip (URIs split on `uriDelimiter`, a sequence, or
  endpoints) is processed as a pipeline on the exchange itself; each send goes through a `ProducerCache` and is wrapped with the route's error
  handler (a failing step is redelivered on its own); the slip stops when the exchange fails or is stopped. `CaravanSlipEndpoint` holds the current
  endpoint. The dynamic router evaluates its expression again after every step until it returns null (or nothing); `CaravanDynamicRouterIndex` (no
  Camel equivalent) numbers the evaluations. As in Camel there is no step limit; a cancelled `Exchange.CancellationToken` ends it with a failure.
- **enrich / pollEnrich** (`EnrichProcessor`, `PollEnrichProcessor`). `enrich` sends a correlated `InOut` copy to the resource (own unit of work
  unless `shareUnitOfWork`), `pollEnrich` receives from a `ConsumerCache`d polling consumer (`timeout`: null/negative blocks, zero does not wait,
  positive waits on the context's clock). A failed reply fails the exchange unless `aggregateOnException`; otherwise the strategy merges it
  (`Aggregate(original, resource)`) and completion callbacks of the resource are handed over. The default strategy, `CopyAggregationStrategy`, takes
  the reply (Camel's behaviour). When `pollEnrich` receives nothing, Camel calls `aggregate(original, null)`; Caravan calls the strategy's
  `IAggregationStrategy.AggregateNoMessage(original)` instead (default: unchanged; `CopyAggregationStrategy`: body set to null, headers kept), so
  `newExchange` is never null.
- **loop** (`LoopProcessor`). Count (evaluated once, `CaravanLoopSize`) or while predicate (checked on the previous output); `CaravanLoopIndex`;
  `copy` starts every iteration from a copy of the input (sharing its unit of work) and keeps the last result. No iteration limit, as Camel; a
  cancelled token fails the exchange, or ends the loop quietly with `breakOnShutdown` (which also stops at context shutdown).
- **delay / sample** (`DelayProcessor`, `SamplingProcessor`). Delays and sample periods use `context.TimeProvider`. `asyncDelayed: false` blocks the
  thread (Camel's synchronous mode); `callerRunsWhenRejected` is accepted but has no effect (no executor). `sample` lets the first exchange of each
  period (or every Nth) through and stops the others (`IsRouteStop`), as Camel.
- **throttle** (`ThrottleProcessor`, `ThrottlingMode`). `ConcurrentRequests` (Camel 4, the default): at most N exchanges hold a permit; the permit
  is an `ISynchronization` (never handed over, e.g. to `seda`) released when the exchange's unit of work is done, so it covers the rest of the
  route, and waiters are released in FIFO order. `TotalRequests` (Camel 3, chosen by `Mode` or by setting `TimePeriod`): a sliding window of N
  admissions per period; an exchange waits until enough admissions are one period old. Both: per-key state (`correlationExpression`; idle keys
  are discarded), the maximum is evaluated per exchange (latest wins; null or non-positive fails), `rejectExecution` fails with
  `ThrottlerRejectedExecutionException`, waiting honours the exchange's cancellation token. A throttle inside a branch that shares its parent's unit
  of work holds the permit until the parent completes.
- **idempotentConsumer** (`IdempotentConsumerProcessor`). Nested block run once per message id. Eager (default) adds the id on arrival
  (atomically: one of concurrent duplicates passes), non-eager checks on arrival and adds on completion. Completion (with the exchange's unit of
  work, or right after the block with `completionEager`) confirms the id on success or removes it on failure (`removeOnFailure`). Duplicates get
  `CaravanDuplicateMessage` and skip the block (or run it with `skipDuplicate: false`).
- **loadBalance** (`LoadBalanceProcessor`). Each step of the block is an output (wrapped as a route node). Built-ins in `Caravan.LoadBalancing`:
  round robin, random, weighted (round-robin or random within each cycle of the distribution ratio; the ratio must match the outputs), sticky
  (correlation value → output, assigned round robin; kept for the processor's lifetime as in Camel), topic (a correlated copy to every output,
  stopping at the first failure; the exchange is unchanged), failover (each attempt on a fresh copy of the input; `maximumFailoverAttempts` -1
  unbounded, `roundRobin` wraps around, `sticky` starts at the last good output, exceptions matched along the cause chain; with
  `inheritErrorHandler` (default) outputs keep the route's error handler, so each is redelivered first and a failure the dead letter channel
  handled does not fail over), and custom.
- **claimCheck** (`ClaimCheckProcessor`). Per-exchange `IClaimCheckRepository` in the `CaravanClaimCheckRepository` property (exchange copies share
  it). `Set`/`Push` store a copy; `Get`/`GetAndRemove`/`Pop` merge a copy back with `ClaimCheckAggregationStrategy` and Camel's filter syntax
  (`body`, `headers`, `header:pattern`, `+`/`-` prefixes; only exclusions means everything else). Keys are Simple expressions (`${header.id}`).
- **validate, script, setExchangePattern, sort, setHeaders**: `PredicateValidationException` when the predicate fails; `script` evaluates for side
  effects; `sort` turns the items of the body (or an expression) into a sorted `List<object?>` (natural ordering coerces types like Camel).

**SPIs.** `IIdempotentRepository` (`Caravan.Spi`): `AddAsync`/`ContainsAsync`/`RemoveAsync`/`ConfirmAsync`/`ClearAsync`, asynchronous so durable
stores fit; `AddAsync` must be atomic; repositories that are `IService`s are started and stopped with the processor. `MemoryIdempotentRepository`
(`Caravan.Idempotent`) keeps the last `MaxCacheSize` (1000) ids added. `ILoadBalancer.ProcessAsync(exchange, processors)`: one instance per node,
called concurrently; `QueueLoadBalancer` is the base for single-output choices; an `IService` load balancer is started with the route.
`IClaimCheckRepository` is synchronous (it lives on the exchange); `MemoryClaimCheckRepository` is the default.

### Resilience: circuit breaker

`.CircuitBreaker(cb => cb.To("direct:remote"), c => c.FailureRatio(0.5).MinimumThroughput(10).SamplingDuration(...).BreakDuration(...)
.Timeout(...).Bulkhead(maxConcurrent, maxWaiting), fallback: f => f.Transform(...))` (`CircuitBreakerDefinition`, `OnFallbackDefinition`,
`CircuitBreakerBuilder`), Camel's `circuitBreaker` with Resilience4j, built on **Polly v8** (`Polly.Core`).

- **Pipeline.** Per node, a `CircuitBreakerCircuit` holds a `ResiliencePipeline<Exchange>`: circuit breaker, then (inside it) the time limit,
  built with `ResiliencePipelineBuilder.TimeProvider = context.TimeProvider` so break, sampling and timeout durations follow the fake clock in
  tests. The bulkhead (a semaphore plus a bounded count of waiters) admits the call before the pipeline; rejections are not counted by the
  breaker, timeouts are. The block runs on a copy of the exchange (same id, same unit of work, a token cancelled on timeout); the call is
  awaited with `WaitAsync(token)`, so a block that ignores cancellation is *abandoned* on timeout (it finishes on its copy, which is dropped). On
  success the copy's results are copied back.
- **Outcomes.** Open circuit → `BrokenCircuitException` (`IsolatedCircuitException` when isolated), timeout → `TimeoutRejectedException`, full
  bulkhead → `BulkheadRejectedException`, or the block's own failure. The fallback (`OnFallback`) then runs on the exchange as it was before the
  breaker, with the exception in `CaravanExceptionCaught`; without a fallback the exchange fails with it. `OnFallbackViaNetwork` runs the fallback
  through a second circuit with the same configuration (`<id>-fallback`). Properties: `CaravanResponseSuccessfulExecution`,
  `CaravanResponseFromFallback`, `CaravanResponseShortCircuited`, `CaravanResponseTimedOut`, `CaravanResponseRejected` (open circuit or full
  bulkhead) and `CaravanCircuitBreakerState`. `RecordExceptions`/`IgnoreExceptions` only choose what the circuit counts; the fallback runs for any
  failure.
- **Configuration.** `CircuitBreakerConfiguration` (all options nullable) merges defaults (failure ratio 0.5, minimum throughput 100, sampling 30 s,
  break 60 s) ← the context default (`SetDefaultCircuitBreakerConfiguration`) ← the named configuration (`ConfigurationRef(name)`: one added with
  `AddCircuitBreakerConfiguration` or a registry bean) ← the node's own options. Resolved when the route is created.
- **State.** Circuits are per node id and live as long as the route's processors (a route stop/start keeps them).
  `context.GetCircuitBreakerStateRegistry()` lists them (`Closed`/`Open`/`HalfOpen`/`Isolated`) and can isolate or close one. Polly reports
  half-open only once the probe call arrives after the break duration.
- **Error handling.** As in Camel the breaker handles failures: nodes inside the block and fallback are not wrapped with the route's error
  handler unless `InheritErrorHandler()`. The `circuitBreaker` node itself is wrapped, but a failure leaving it is flagged so that its error handler
  goes straight to `onException`/the dead letter channel without redelivering the call.

Differences from Camel: Polly's breaker is time-based (Resilience4j defaults to a count-based window) and lets one probe through in half-open;
timeouts count as failures (Resilience4j's time limiter is outside the breaker); the bulkhead has a waiting queue length instead of a maximum
wait duration; `onFallbackViaNetwork` (Hystrix only in Camel) is kept with the meaning above.

### Sagas

`.Saga(s => s.Propagation(SagaPropagation.Required).Compensation("direct:undo").Completion("direct:done").Timeout(...)
.CompletionMode(SagaCompletionMode.Auto).Option("id", Expr.Header("orderId")), body => body.To(...))` (`SagaDefinition`, `SagaBuilder`,
`SagaProcessor`), Camel's LRA-style `saga` EIP with its in-memory saga service.

- **Current saga.** The `Long-Running-Action` header (`CaravanHeaders.SagaLongRunningAction`) names it; a saga the service no longer knows
  (finished) counts as none. Propagation, as Camel: `Required` joins or starts one, `RequiresNew` always starts one (the current header is
  restored after the block), `Mandatory` fails without one (`CaravanExchangeException`), `Supports` joins when there is one, `NotSupported`
  removes the header for the block, `Never` fails when there is one. Joining or starting adds a `SagaStep` (compensation and completion endpoints,
  option expressions evaluated now, timeout) with `ISagaCoordinator.BeginStepAsync`.
- **Ending.** Only the EIP that *started* a saga ends it. `Auto`: when the block ends, `CompleteAsync` if the exchange succeeded,
  `CompensateAsync` if it failed (exception or rollback), awaited by the exchange; then the previous header is restored. `Manual`: the saga and its
  header stay until `saga:complete`/`saga:compensate` (`SagaComplete()`/`SagaCompensate()`, the `saga` component) or the timeout. The chained form
  `.Saga(s => ...)` without a block makes the rest of the exchange the scope: an `Auto` saga ends through an `ISynchronization` when the unit of work
  completes. Completing a compensated saga (or the reverse) throws `InvalidOperationException`, which fails the exchange.
- **SPI** (`Caravan.Saga`). `ISagaService` (`NewSagaAsync`, `GetSagaAsync`) and `ISagaCoordinator` (`BeginStepAsync`, `CompleteAsync`,
  `CompensateAsync`), asynchronous so an LRA coordinator fits. The context's service: `SetSagaService`, else the only `ISagaService` bean, else an
  `InMemorySagaService` created on first use; the first route using it adds it to the context's services (started and stopped with the context).
- **InMemorySagaService.** Steps' endpoints are called last step first, each with a new `InOnly` exchange whose headers are the step's option
  values plus `Long-Running-Action`; a failing call is retried `MaxRetryAttempts` (5) times, `RetryDelay` (5 s) apart on the context's clock,
  then logged (the saga stays `Completing`/`Compensating`). A step timeout is an `ITimer` on the context's clock: a still running saga is then
  compensated in the background (the service's stop waits for it). Finished sagas are forgotten.

Differences from Camel: the block form scopes the saga to the block (Camel's `saga()` covers the rest of the route — the chained form does
that); `MaxRetryAttempts` counts retries after the first call.

### Interception

Camel's three interceptors, defined on a `RouteBuilder` (`b.Intercept(...)`, `b.InterceptFrom(...)`, `b.InterceptSendToEndpoint(...)`) and
applied to that builder's routes. `RouteBuilder.Build` copies them into `RouteDefinition.Intercepts`, `InterceptFroms` and
`InterceptSendToEndpoints` (as with builder-level `onException`); the lists are public so that `AdviceWith` can add to them; route
configurations add theirs when the route is created (`RouteContext.Intercepts` etc., see "Route configuration"). Blocks are `RouteSteps` builders (`InterceptBuilder`, `InterceptSendToEndpointBuilder`) with `When(predicate)`;
`Stop()` in a block stops the exchange.

- **intercept** runs before every node of the route, nested nodes included (choice branches, split parts, multicast branches), but not the
  nodes of intercept, `onException` and `onCompletion` blocks. `CaravanContext.CreateRoute` builds each block once per route (route service),
  then adds an `InterceptNodeStrategy` (`IInterceptStrategy`) to `RouteContext.InterceptStrategies` *before* the steps are built, so
  `CreateNodeProcessor` wraps every node in an `InterceptProcessor`: if `When` matches, it sets `CaravanInterceptedRouteId`/`NodeId`/
  `RouteEndpointUri`, runs the block and runs the node unless the block failed, stopped, rolled back or handled the exchange
  (`RoutingFlags` compares the exchange's flags before and after the block). The interceptor sits outside the node's error handler, but the
  block's own nodes are wrapped by the route's error handler like any node, so a failing block is redelivered/handled per `onException` or
  the dead letter channel.
- **interceptFrom(pattern?)** wraps the route's steps (`InterceptFromProcessor`, inside `OnCompletionProcessor`) when the route's endpoint
  matches the pattern (`EndpointHelper.MatchEndpoint`: exact after normalization, wildcard or regular expression, also without query
  parameters; null = all routes). It sets the header and property `CaravanInterceptedEndpoint` to the route's endpoint URI and runs on every
  entry into a route, also for a `direct` call from another route.
- **interceptSendToEndpoint(pattern, skipSendToOriginalEndpoint, afterUri)** is universal, as in Camel where the endpoint itself is
  decorated: producers are created through `SendToEndpointInterceptors.CreateProducer(endpoint)` (by `SendProcessor`, hence `to`, every
  `ProducerCache` user — `toD`, `recipientList`, `wireTap`, `enrich`, `routingSlip`, dead letter channels — and by producer templates). The
  returned `InterceptSendToEndpointProducer` reads the context's `SendToEndpointInterceptors` on every send (a lock-free versioned
  snapshot; the chain of interceptors matching its endpoint is cached per version), so interceptors added later, e.g. by `AdviceWith`, apply
  to existing producers too; with none registered a send costs one array read. When a route is created, each of its
  `InterceptSendToEndpointDefinition`s is registered once in the context as an `InterceptSendToEndpointInterceptor` (its block and after
  endpoint are built with a route context of their own and run as a context service). Per send: the header `CaravanInterceptedEndpoint` is
  set, the block runs if `When` matches, then — unless the block failed or stopped the exchange, or `skipSendToOriginalEndpoint` and `When`
  matched — the original producer runs, then `afterUri` (with the original endpoint's reply). The block runs inside the sending node, so the
  sender's error handler covers it. Sends made by a block are not intercepted again by the same interceptor (an exchange property guards
  it), so `interceptSendToEndpoint("mock:*", i => i.To("mock:audit"))` does not loop.

Differences from Camel: the intercepted endpoint is not a separate endpoint object (`GetEndpoint` returns the real endpoint and
interception happens in its producers); `afterUri` runs only when the original endpoint was called; `interceptFrom` also sets the
intercepted endpoint as an exchange property; intercepts skip `onException`/`onCompletion` blocks; `ExchangeSending`/`ExchangeSent` events
of a `to` refer to the original endpoint even when it was skipped.

### Testing toolkit

`src/Caravan.Testing` (namespace `Caravan.Testing`, depends on `Caravan.Core` and `Microsoft.Extensions.TimeProvider.Testing`, no test
framework):

- **AdviceWith** (`AdviceWith.RouteAsync(context, routeId, a => ...)` / `context.AdviceWithAsync(...)`) changes a route's *definition*.
  Nodes without an id first get their generated id (`NextNodeId`), so ids stay stable across advice and restarts. The lambda records
  operations, applied in order when it returns: `ReplaceFromWith(uri)`; `WeaveById/ByToUri/ByToString/ByType<T>(...)` returning an
  `AdviceWithBuilder` (`SelectFirst/Last/Index/Range`, `MaxDeep(n)` — 1 = top level; default: every match at any depth, found by a
  depth-first walk of `ProcessorDefinition.Children`) with `Replace/Remove/Before/After(r => ...)`; `WeaveAddFirst()/WeaveAddLast()`;
  `MockEndpoints(patterns)` / `MockEndpointsAndSkip(patterns)`; and `Intercept`/`InterceptFrom`/`InterceptSendToEndpoint` added to the route.
  Weaving edits the list holding the node (the route's `Outputs`, a definition's `Outputs`, a choice's `WhenClauses`, ...; found by
  reflection over the parent's list properties) or a single-valued slot (`Otherwise`, `Finally`, `OnFallback`: replace with one node or
  remove); a weave matching nothing fails. Before the context starts the definition is changed in place and created at start; on a started
  context the route is stopped, removed, changed and added again (`AddRoutesAsync`), so it restarts with new processors that pick up the
  route's error handler, `onException`, `onCompletion` and intercepts as usual.
- **Mocking endpoints** (`MockEndpointsInterceptor`, a `ISendToEndpointInterceptor`): every send to a matching endpoint first goes to
  `mock:<normalized uri without parameters>` (`EndpointHelper.MockEndpointUri`, e.g. `mock:direct:foo`), then to the endpoint unless
  skipping. `mock:` endpoints are never mocked. Registered context-wide, once per pattern set.
- **NotifyBuilder** listens to exchange events (`IEventNotifier`; `CaravanContext.EventNotifiers` is a copy-on-write list, so notifiers can be
  added while exchanges are routed). Predicates form groups combined with `And()/Or()/Not()`; within a group, events flow through the
  predicates in order and filters (`From`, `FromRoute`, `Filter`, `WereSentTo` — the latter inserted at the start of its group, as Camel)
  stop events of other exchanges. Conditions: `WhenReceived/Done/Completed/Failed(n)`, `WhenExactlyDone/Completed/Failed(n)`,
  `When(Exact)BodiesReceived/Done(...)`, `WhenAny/AllReceivedMatches`, `WhenAny/AllDoneMatches`. Received = `ExchangeCreated` (a route created
  the unit of work), done = `ExchangeCompleted`/`ExchangeFailed` of that unit of work: an exchange crossing `direct` routes counts once, a
  `seda` hand-over counts again in the consuming route (new exchange id). `MatchesAsync(timeout)` waits in real time for the first match and
  returns the current state.
- **CaravanTestSupport**: an `IAsyncDisposable` base class: `SetUpAsync()` creates the context (`CreateCaravanContext`), sets the shutdown
  timeout, the fake clock (`UseFakeTimeProvider` → `FakeTimeProvider`), registry beans (`BindToRegistry`), endpoint mocking
  (`IsMockEndpoints`/`IsMockEndpointsAndSkip`), adds `CreateRouteBuilder(s)` and starts the context unless `UseAdviceWith`; members
  `Context`, `Template`, `ConsumerTemplate`, `AdviceWithAsync`, `StartAsync`, `GetMockEndpoint(uri, create)`,
  `AssertMockEndpointsSatisfiedAsync`, `ResetMocks`. With xUnit, implement `IAsyncLifetime` explicitly and call `SetUpAsync`/`DisposeAsync`.
- **Mock endpoint** (`Caravan.Core`): besides counts, bodies and headers, fluent clauses `Message(i)` / `AllMessages()` /
  `ExpectedBodyReceived()` with `Body()`, `Body<T>()`, `Header(n)`, `Property(n)`, `Value(expr)` and `IsEqualTo`, `IsNull`,
  `IsGreaterThan`, `Contains`, `Regex`, `Matches(...)` (Camel type coercion); `ExpectedMessagesMatches`, `ExpectsAscending/Descending`,
  `ExpectsNoDuplicates`, `ExpectedPropertyValuesReceivedInAnyOrder`, `ExpectedExchangePattern`, `Expects(Action)`; `ReturnReplyBody` /
  `ReturnReplyHeader` (applied after `WhenAnyExchangeReceived`); `AssertExchangeReceived(i)`, `GetExchanges()`; `ResultWaitTime`,
  `ResultMinimumWaitTime` (fails when the expected messages arrive sooner than that after the assertion started) and `AssertPeriod` (re-checks
  after the count is reached to catch late extra messages).

Differences from Camel: AdviceWith uses lambdas for the replacing steps (`Replace(r => r.To(...))` instead of `replace().to(...)`);
`WeaveByToString` matches the node's `Label` (`to[mock:a]`) or `ToString()`; NotifyBuilder's bodies conditions check every position (Camel
only the latest); the test base class takes properties (`IsMockEndpoints`, `UseAdviceWith`) instead of overridable `isXxx()` methods and
needs the test framework to call `SetUpAsync`.

### Languages and the Simple language

**Languages SPI.** `ILanguage` (`Caravan.Spi`: `CreateExpression(text)`, `CreatePredicate(text)`, thread-safe) is resolved by name with
`context.ResolveLanguage(name)` (`LanguageRegistry`, `ICaravanContext.Languages`): languages added to the context (`Languages.Add`), then an
`ILanguage` bean bound under the name, then global ones (`LanguageRegistry.RegisterGlobal`). Built-ins (`Caravan.Language`): `simple`,
`constant` (the text), `header` and `exchangeProperty` (the named value), `tokenize` (the body tokenized on the text), `ref` (a registry
`IExpression`/`IPredicate`, looked up when evaluated), `bean`/`method` (a bean method's result, see "Bean binding"). DSLs that read routes from text (YAML, row 14) resolve expressions this way.
`Lang` (`using static Caravan.Builder.Lang;`) has terse factories: `Simple(text)`, `Constant`, `Body`, `Header`, `ExchangeProperty`,
`Tokenize`, `Language(name, text)`.

**Simple** (`Caravan.Language.SimpleLanguage`, internals in `Caravan.Language.Simple`). `SimpleParser` turns text into an immutable tree of
`SimpleNode`s, evaluated concurrently; compiled texts are kept in LRU caches (`SimpleLanguage.CacheSize`, default 1000, as Camel) keyed by
text, so `Expr.Simple(text)` returns the cached `SimpleExpression` and `Pred.Simple(text)` the cached `SimplePredicate`.
`Lang.Simple(text)` returns a `SimpleBuilder`, both an `IExpression` and an `IPredicate` (Camel's `simple()`): the expression form is
compiled at once, the predicate form when the route is created (`SimpleRouteValidator` walks every `IPredicate` property of the route's
nodes), so a malformed predicate fails route creation. Errors are `SimpleParseException` (`Reason`, `Expression`, zero-based `Index`; the
message ends with the text and a `*` under the position); evaluation failures (missing member, null without `?.`, bad index) are
`ExpressionEvaluationException`. `{{placeholders}}` in a Simple text are resolved with the exchange's context before evaluation (the
resolved text is compiled and cached), so they work in `log`, `toD`, `setBody(simple(...))` and so on.

- **Expressions** are text mixed with functions `${...}` (or `$simple{...}`); a text made of one function evaluates to the function's value
  (not a string), anything else to a string (null renders as nothing). Escapes: `\n`, `\t`, `\r`, `\${` (a literal `${`), `\}`, `\'`,
  `\"`; other backslashes are kept. `$${body}` is a `$` followed by the body. `${function}++`/`--` (followed by whitespace or the end)
  increments/decrements the value, converted back to its type, and writes it back to the body, header or exchange property.
- **Functions**: `body`, `in.body`, `bodyType`, `bodyOneLine`, `bodyAs(Type)`, `mandatoryBodyAs(Type)` (`InvalidPayloadException`),
  `header.name` / `header:name` / `header[name]` / `headers.name` / `in.header(s).name`, `headerAs(name,Type)`, `headers`,
  `exchangeProperty.name` / `[name]`, `exchangePropertyAs(name,Type)`, `exchangeId`, `id`/`messageId`, `routeId`, `fromRouteId`,
  `camelId`/`contextName`, `stepId`, `exchange`, `exception` (the exception, or `CaravanExceptionCaught`), `exception.message`,
  `exception.stacktrace`, `date:command[:pattern]`, `date-with-timezone:command:zone:pattern`, `properties:key[:default]`,
  `propertiesExist:[!]key`, `random(max)`/`random(min,max)`, `uuid` (the context's `UuidGenerator`; `uuid:default|classic|random|short|simple`), `null`,
  `empty(string|list|map|set)`, `env:NAME`/`env.NAME`/`sysenv.NAME`, `sys.name`, `collate(n)`, `skip(n)`, `join(separator,prefix,source)`,
  `type:Full.Type.Name.Member` (public static field, constant or property), `ref:bean`, `threadId`, `threadName`, `hostname`.
  Type names accept C# keywords, Java names (`String`, `Integer`, `java.lang.Long`, `BigDecimal`, `List`, `Map`), full names and unique
  simple names of loaded types, and `[]`.
- **OGNL** on `body`, `bodyAs(...)`, `header`, `exchangeProperty`, `exception` and `exchange`: `.Property` (exact, then case-insensitive;
  public fields too; `size`/`length` read `Count`/`Length`), `.Method(args)` (constant arguments converted to the parameter types; Java
  getters `getName()`/`isActive()` read properties), `[index]` on lists, arrays and other sequences (`last`, `last-1`), `[key]`/`['key']`
  on dictionaries (also `.key` when there is no such property), indexers. A null root yields null; a null later in the path fails unless
  the step is null-safe (`?.`, `?[`). For headers and properties the full name is tried first (`${header.a.b}` reads the header `a.b`
  if it exists). Nested functions build the function text at run time: `${header.${header.name}}`, `${body.Substring(${header.n})}`.
- **Dates**: commands `now`, `millis` (Unix milliseconds), `exchangeCreated`, `header.x`, `exchangeProperty.x` (a `DateTimeOffset`,
  `DateTime` — unspecified kind is UTC —, Unix milliseconds or a parsable string), with offsets `now-1h30m`, `header.due+2d` (units `d h m
  s ms`). Without pattern the value is a `DateTimeOffset` (or `long` for `millis`); patterns are Java `SimpleDateFormat` (`yyyy MM dd HH
  mm ss SSS`, `EEE`, `MMM`, `a`, `Z`, `X`, `'quoted'`, ... plus .NET `f`), formatted in `context.TimeProvider.LocalTimeZone` (UTC with
  a `FakeTimeProvider`) or the given IANA/Windows zone. Time comes from `context.TimeProvider`, so tests use the fake clock.
- **Predicates**: operands (functions, quoted text that may contain functions, numbers, `true`/`false`, `null`, other unquoted words as
  text) joined by whitespace-separated operators: `==`, `=~` (ignore case), `!=`, `!=~`, `>`, `>=`, `<`, `<=`, `contains`, `!contains`,
  `~~` (contains ignoring case), `!~~`, `regex`, `!regex` (whole match), `in`, `!in` (comma list or collection), `range`, `!range`
  (`'1..10'`, inclusive), `is`, `!is` (type), `startsWith`, `!startsWith`, `endsWith`, `!endsWith`, combined with `&&` and `||` evaluated
  left to right with equal precedence and short-circuiting (Camel). Values are coerced with `ObjectHelper` (`'123' == 123`); comparing
  with null is false except `null >= null` / `null <= null`; a lone operand is converted with `PredicateHelper.ToBoolean`.
- **Inside a function** the same operators apply to bare functions and literals, plus the ternary `${header.n > 0 ? 'pos' : 'neg'}`
  (nestable) and the elvis `${header.x ?: 'default'}` (the fallback when the value is null, empty or `false`). A function text is split on
  these operators only when one appears as a separate word, so `${date:now:dd MMM yyyy}` stays one function.
- **Extension point**: `SimpleLanguage.RegisterFunction(prefix, remainder => IExpression)` adds `${prefix:...}` functions; the `bean:`
  function (`${bean:name?method=foo}`, `${bean:name.foo}`, `${bean:class:Full.Type?method=foo}`) is registered this way by `SimpleLanguage`'s
  static constructor (`BeanExpression.Parse`).

Differences from Camel: no `messageHistory`, `messageTimestamp`, `date:file`, `variable(s)`, `jsonpath`/`xpath` or the newer string
functions; `sys.name` reads `AppContext.GetData`; `exception.stacktrace` is `Exception.ToString()`; `collate`/`skip`/`join` split a string
body on commas (Camel's iterator) unlike `split`; unquoted words inside `${...}` are function names, not text (quote literals there);
`$${` is not an escape (use `\${`); unknown escapes keep the backslash; header names containing `+`/`-` cannot be used in `date:` commands
(those characters start offsets, as in Camel); OGNL member names match case-insensitively as a fallback for Java-style names.

### Properties

`ICaravanContext.PropertiesComponent` (`IPropertiesComponent`, default `Impl.DefaultPropertiesComponent`) resolves placeholders:
`{{key}}`, `{{key:default}}` (empty default allowed), nested keys (`{{app.{{env}}.url}}`), values and defaults containing placeholders
(cycles fail with "Circular reference"), `{{env:NAME[:default]}}` (also tried upper-cased with `.`/`-` → `_`), `{{sys:name}}`
(`AppContext.GetData`) and optional `{{?key}}`: `ParseUri` removes the query option whose value uses a missing optional placeholder, other
text gets an empty string. A missing key without default throws `ArgumentException` ("Property with key [x] not found ...").
Precedence: `OverrideProperties` → environment variables if `EnvironmentVariableMode.Override` → `Sources` in order (=
`ICaravanContext.PropertiesSources`; `AddConfiguration(IConfiguration)` adds a `ConfigurationPropertiesSource`, which reads `app.queue`
then `app:queue`) → `InitialProperties` → environment variables if `Fallback` → the default. `EnvironmentVariableMode` defaults to `Never`
(in .NET environment variables usually arrive through `IConfiguration`'s provider; Camel defaults to override). A context created with an
`IServiceProvider` that has an `IConfiguration` uses it as a source. API: `ResolveProperty(key)` (null when missing), `Resolve(key)`,
`ResolvePlaceholders(text)`, `ParseUri(uri)`; `context.ResolvePropertyPlaceholders(text)` is `ParseUri`.

Route templates substitute their parameters into a copy of the template's route before any of this (see "Route templates"), so a
parameter works wherever a placeholder does and wins over a property of the same name.

Where placeholders are resolved: endpoint URIs (every `GetEndpoint`: `from`, `to`, dynamic URIs, endpoint options), Simple expressions and
predicates (so `log` messages and every dynamic string option), route ids, groups and descriptions and node ids/descriptions (when routes are
added), the `log` EIP's logger name, registry references (`aggregationStrategyRef`, ...), and the `properties:` Simple function. Not
covered: the `properties:` endpoint component and encrypted values.

### Data formats

`IDataFormat` (`Caravan.Spi`): `MarshalAsync(exchange, graph, output)` writes an object to a stream, `UnmarshalAsync(exchange, input)` reads one.
Instances are configured once and then shared by all exchanges (thread-safe); an `IService` data format is started with the route using it.
`IDataFormatContentType` (`ContentType`, `ContentTypeHeader`, default true) makes `marshal` set the `Content-Type` header (json, xml).

- **Resolution.** `context.ResolveDataFormat(name)` / `ICaravanContext.DataFormats` (`DataFormatRegistry`): an instance added with `Add` (shared) →
  an `IDataFormat` registry bean → a new instance from a factory (`AddFactory`, then the global `RegisterGlobal` ones) → `NoSuchDataFormatException`.
  Names accept placeholders.
- **EIPs.** `.Marshal(df)` / `.Marshal("json")`, `.Unmarshal(df, allowNullBody)` / `.Unmarshal("csv")` (`MarshalDefinition`/`UnmarshalDefinition`,
  names resolved when the route is created). `MarshalProcessor` replaces the body with the written `byte[]` (headers kept);
  `UnmarshalProcessor` reads the body as a stream (text and bytes are converted; a stream cache is reset), disposes the input unless the
  data format returned it, and fails a null body with `InvalidPayloadException` unless `allowNullBody`.
- **Component.** `dataformat:name:marshal|unmarshal?options` resolves the data format and binds the options to its properties (`PropertyBinder`:
  `unmarshalType=Full.Type.Name`, `delimiter=;`, ...). Producer only.
- **Built-ins** (`Caravan.DataFormat`, factories on `Caravan.DataFormats`, BCL only): `json`/`jackson` (`JsonDataFormat`, System.Text.Json with
  `JsonSerializerDefaults.Web`; `UnmarshalType`, `UseList`, `PrettyPrint`, `Include` = `ALWAYS`/`NON_NULL`/`NON_DEFAULT`, `SerializerOptions`,
  `AllowUnmarshallType` with the `CaravanJacksonUnmarshalType` header; untyped unmarshal gives `Dictionary<string, object?>`/`List<object?>`/
  `string`/`bool`/`int`/`long`/`double`/null, like Jackson's maps, so Simple OGNL works on it; `typeof(JsonNode)` for a DOM),
  `xml`/`jaxb` (`XmlDataFormat`, `XmlSerializer` per runtime type without `xsi`/`xsd` declarations; untyped unmarshal gives an `XDocument`;
  `PrettyPrint` (default true, as JAXB), `Fragment`, `Encoding`; DTDs prohibited), `csv` (`CsvDataFormat`, RFC 4180: `Delimiter`, `Quote`,
  `Escape`, `RecordSeparator` (CRLF), `Header`, `SkipHeaderRecord`, `UseMaps`, `IgnoreEmptyLines`, `Trim`, `QuoteAll`; unmarshal gives
  `List<List<string>>` or `List<Dictionary<string, string?>>`), `base64` (`LineLength` 76 with a trailing separator as commons-codec, `UrlSafe`
  without padding), `gzipDeflater`/`gzip` (`GZipStream`), `zipDeflater` (`ZLibStream`, Java's `Deflater` format), `string` (`Charset`).

Differences from Camel: `marshal` produces a `byte[]` (Camel a stream cache); JSON uses .NET's camelCase convention and System.Text.Json;
there is no `serialization` data format (binary serialization is unsafe and obsolete in .NET), no `zipFile`, no fluent `marshal().json()`
clause (use `DataFormats.Json()`).

### Stream caching

`ICaravanContext.StreamCaching` (`Caravan.Streams.StreamCachingStrategy`), Camel's stream caching. Enabled by default for every route (Camel 4);
`.StreamCaching(bool)` on a route (`RouteDefinition.StreamCaching`) overrides the context's `Enabled`.

- **Where.** `Channel` runs before every node: when the route caches streams and the body is a `Stream`, an `IStreamCache` body is reset,
  another stream is read into a new cache (and disposed) which replaces the body. So every node reads the whole body, and a stream produced by
  a node is cached before the next one. Unlike Camel any stream is cached, seekable or not.
- **Storage.** `CachedOutputStream` buffers in memory; with `SpoolEnabled` (default false, as Camel 4) data beyond `SpoolThreshold` (128 KB) goes
  to a temporary file in `SpoolDirectory` (default `<temp>/caravan/caravan-tmp-<uuid>`). The result is a `ByteArrayStreamCache` or a
  `FileStreamCache` (read-only, seekable `Stream`s). A spool file is an `ISynchronization` on the exchange that created it, deleted when its
  unit of work completes (handed over with the exchange, e.g. to `seda`); the directory is deleted when the context stops
  (`RemoveSpoolDirectoryWhenStopping`). Counters: `CacheMemoryCounter`/`Size`, `CacheSpoolCounter`/`Size`, `SpoolFilesDeleted`.
- **Copies.** `Message.CopyFrom` (hence `Exchange.Copy`, correlated copies of split/multicast/wire tap branches, seda copies) gives the copy
  `IStreamCache.Copy()`: an independent reader over the same data, so parallel branches never share a position. File readers open their handle
  when created and keep reading after the file was deleted (`FileShare.Delete`); disposing a reader only closes its handle, reopened on demand
  while the file exists. Disposing a byte array cache does nothing.
- **Readers.** The type converters read a stream cache from the start and reset it (`Stream` → `byte[]`/`string`), so `${body}` in a log, a
  `convertBodyTo` or `GetBody<string>()` can run any number of times; `ExchangeFormatter` shows cached bodies.

### Bean binding

The `bean` EIP (`.Bean(object | Type | "name", method?, scope?)`, `BeanDefinition`, `BeanProcessor`), the `bean:name?method=...&scope=...` and
`class:Full.Type.Name?method=...` components (`bean.property=value` options set on the bean) and the `bean`/`method` language (`Expr.Method(...)`,
`${bean:...}`, `BeanExpression`) share Camel's bean binding (internals in `Caravan.Components.Bean`):

- **Bean.** An instance; a registry bean by name (placeholders allowed; falls back to the service provider's service for a type of that name);
  or a type: the context's `IServiceProvider` service, else a constructor whose parameters come from the service provider, the context or
  defaults (`BeanActivator`); a static class has only its static methods. `BeanScope`: `Singleton` (once per node/endpoint/context; a missing
  registry bean or unknown method fails route creation), `Request` (once per exchange, kept in an exchange property), `Prototype` (every call).
  An `IProcessor` bean without method processes the exchange itself.
- **Method.** `method=name` (exact case, then ignoring case), `name(string, int)` (parameter types choose the overload; `*` matches any type;
  C#/Java/full type names), or `name(${body}, 'text', 5, true, null)` (one value per parameter: Simple expressions evaluated per call, quoted
  text, numbers, booleans, null; other words are text). Without a name the candidates are the public methods (not `object`'s, accessors,
  `Dispose`...), only the `[Handler]` ones if any. One candidate is taken; otherwise by the body: the methods with one body parameter the body is
  an instance of (most specific wins), else the only one the body converts to, else the only method without body parameter; else
  `AmbiguousMethodCallException`. No match: `MethodNotFoundException`. Choices depending only on the body's type are cached per type
  (`BeanInfo` per bean type, a selector per method specification).
- **Parameters.** `[Body]`, `[Header("n")]`, `[Headers]`, `[ExchangeProperty("n")]`, `[ExchangeProperties]`, `[ExchangeException]` (the exception or
  caught exception, or the first of the parameter type in its cause chain), `[Simple("...")]`; by type: `Exchange`, `Message`, `ICaravanContext`,
  `CancellationToken` (the exchange's), `ITypeConverter`/`TypeConverterRegistry`, `IRegistry`; the first other parameter takes the body, later
  ones get their default value. Values are converted with the type converters; a failure is a `ParameterBindingException`.
- **Invocation.** A compiled delegate per method; `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` are awaited; exceptions are not wrapped. The
  result becomes the body whatever the exchange pattern (null included); `void`/`Task`/`ValueTask` leave the body unchanged. `BeanExpression`
  returns the result without changing the body (asynchronous methods are waited for synchronously).

Differences from Camel: no OGNL method chains in the bean language, no `CamelBeanMethodName` header, parameters after the body parameter get
defaults instead of being matched by type; method names fall back to a case-insensitive match; `.Bean(type)` asks the service provider first.

### Misc components

- **controlbus** (`controlbus:route?routeId=x|current&action=start|stop|suspend|resume|restart|status|stats&async=..&restartDelay=..`,
  `controlbus:language:name`): `status` returns the route (or, without `routeId`, context) status name, `stats` an XML `routeStat` /
  `camelContextStat` with the route counters (`Route.ExchangesTotal`/`Completed`/`Failed`, inflight); `async` runs the action in the background
  (required for a route stopping itself). The language command evaluates the body as an expression and sets a non-null result as the body.
- **stub** (`StubComponent : SedaComponent`): a `seda` queue named after the whole path (`stub:jms:queue:x`), lenient options, own queues.
- **ref** (`ref:name`): the registry endpoint (an `EndpointBase` as is, another `IEndpoint` wrapped in `RefEndpoint`, a string URI resolved).
- **dataset** / **dataset-test**: `IDataSet` (`Size`, `ReportCount`, `PopulateMessage`, `AssertMessageExpected`), `DataSetSupport`, `SimpleDataSet`,
  `ListDataSet`. `DataSetEndpoint` is a `MockEndpoint` expecting `Size` messages: its consumer sends them after `initialDelay` (1 s) and once the
  data set has `minRate` messages, `produceDelay` (3 ms) apart, with the `CaravanDataSetIndex` header; its producer checks order (`dataSetIndex`
  lenient/strict/off) and content per message after `consumeDelay` (`MockEndpoint.PerformAssertionsAsync`). `dataset-test:uri` reads the
  expected bodies from `uri` when it starts (polling until `timeout`, 2 s), optionally `split`, and expects them (`anyOrder`).
- **browse** (`browse:name?browseLimit=n`): records a copy of each exchange (`Exchanges`), then hands it to its consumer like `direct`.

### Route templates

Camel's route templates (and the base of Kamelets). `RouteBuilder.RouteTemplate("greet")` returns a `RouteTemplateBuilder`:
`.TemplateParameter(name, defaultValue?, description?, required = true)` (a default makes it optional), `.TemplateOptionalParameter(name)`,
`.TemplateBean(name, rtc => new X(...) | typeof(X))`, `.Configure(rtc => ...)`, then `.From(uri)` and the steps. Templates live in the
context (`RouteTemplateDefinitions`, `AddRouteTemplateDefinition` — same id replaces —, `RemoveRouteTemplateDefinition`), never start, and
get the error handler, `onException`, `onCompletion` and interceptors of their route builder like its routes.

- **Creating routes.** `context.AddRouteFromTemplateAsync(routeId, templateId, parameters | RouteTemplateContext)`,
  `TemplatedRouteBuilder.Builder(context, id).RouteId(..).Parameter(..).Bean(name, obj | factory).Configure(rtc => ..).Handler(route => ..).AddAsync()`,
  or declaratively `RouteBuilder.TemplatedRoute(id, r => r.RouteId(..).Parameter(..))` (the template may come from the same builder — a
  builder's configurations and templates are added before its routes — or from one added earlier, e.g. a library). A route created before the
  context starts is created at start; on a running context it is created and started at once. The steps: defaults are added for parameters
  without value, the template's then the caller's configurer run, missing required parameters (null counts as missing) fail with
  `FailedToCreateRouteFromTemplateException` ("... the following mandatory parameters must be provided: a, b"; unknown parameters are
  ignored, as Camel); the route id is the given one, else the template route's id (after substitution), else generated; the beans are
  created; then the template's route is **deep-copied** with the parameters substituted, and `Handler` may change the copy. The route records
  `TemplateId`, `TemplateParameters` and `LocalBeans`.
- **Substitution** (`TemplateParameterSubstitution`). Every string of the copy (URIs, ids, descriptions, options, properties, route
  configuration ids) and the text of every Simple expression/predicate (`Expr.Simple`, `Pred.Simple`, `Lang.Simple`, recompiled when changed)
  gets `{{p}}`, `{{p:default}}` and `{{?p}}` replaced when `p` is a parameter (case-insensitive) with the value's invariant text; nested keys
  work (`{{app.{{env}}.url}}`). Other placeholders are left to the properties component as usual, so template parameters take precedence
  over context properties (Camel's local properties) and an optional parameter without value falls back to them (`{{?p}}` in a URI then drops
  the option). Expressions other than Simple (lambdas, constants) are shared, not rewritten.
- **Deep copy** (`DefinitionCloner`, also public as `RouteDefinition.Clone()` / `ProcessorDefinition.Clone()`). Reflection over instance
  fields: `ProcessorDefinition`s, `RouteDefinition`s and the other `Caravan.Model` classes (error handlers, redelivery policies, resequencer
  configurations, ...) are copied, each once (sharing inside the graph is kept); `List<>`, `Dictionary<,>`, `HashSet<>` and arrays are copied
  with copied elements; `Parent`/`Route` are remapped to the copies (cleared when outside the graph); expressions, predicates, delegates,
  types, strategies and beans are shared. New EIP definitions need no copy code as long as they keep their state in fields of those kinds.
  So every route owns its nodes: `AdviceWith`, `Handler` or code changing one route never affects the template or the other routes, and node
  ids (made unique with placeholders such as `.Id("send-{{name}}")` when needed) are per route.
- **Local beans** (`LocalBeanRegistry`, Camel's local bean repository). Template beans (created per route, with the `RouteTemplateContext`
  to read parameters: `rtc.GetProperty<int>("start")`) and beans given per route are bound in the route's `LocalBeans`. The context's
  `DefaultRegistry` looks names up there first while the route is created (`CaravanContext.CreateRoute`, route start) and while it processes
  an exchange (`RouteProcessor` sets `LocalBeanRegistry.Current`, an `AsyncLocal` that follows the route's asynchronous calls and is reset
  when the exchange enters a route without local beans, e.g. a `direct` call). So `.Bean("myBean")`, `ProcessRef`, `...Ref` options and
  `#myBean` see the route's instance. Each bean is also bound under a name unique to the route, `{name}-{routeId}`, and the parameter `name`
  is set to it (unless given): since endpoints and compiled Simple texts are cached context-wide by text, a template refers to a local bean
  in a URI or Simple text through its placeholder — `bean:{{myBean}}`, `?x=#{{myBean}}`, `${bean:{{myBean}}?method=..}` — so each route gets
  its own endpoint/expression. Lookups from threads that are not on the route's flow (e.g. an aggregator's timeout sender) do not see them.
- **Parameter sources** (`IRouteTemplateParameterSource`: `RouteIds()`, `Parameters(routeId)` including `templateId`). When the context
  starts, the sources in `context.RouteTemplateParameterSources` and the registry create their routes, skipping ids that exist (restart).
  `ConfigurationRouteTemplateParameterSource` reads an `IConfiguration` section with one child per route (key or `routeId` = route id,
  `templateId`, parameters under `parameters` or as the other values); a context created with a service provider whose configuration has
  `caravan:templatedRoutes` uses it automatically (Camel main's `camel.route-template`).
- **Kamelets** (`kamelet` component, producer side). A template consuming from `kamelet:source` is called with
  `to("kamelet:templateId[/routeId]?param=v")`: the endpoint (one per distinct URI, i.e. per parameter set) creates the route once, on the
  first send (`routeId` option/path, else `{templateId}-{n}`; parameters missing from the URI come from the properties
  `caravan.kamelet.{templateId}.{param}`, then the defaults), its `from` becoming `kamelet:source?routeId=<id>`; the exchange is then
  processed by that route synchronously, like `direct`, and returns to the caller when it ends. `kamelet:sink` marks the end and does
  nothing. Kamelet routes are never removed by route filters. Templates can be packaged in a library's `RouteBuilder` and used from any
  context that adds it.

Differences from Camel: substitution happens once, in the copy, rather than through local properties at reification (same result for strings
and Simple, but lambdas cannot see parameters — use `rtc` in a bean factory or configurer); local beans also get a unique name (Camel binds
them under their name only, which a context-wide endpoint cache would share between routes); `TemplateBean` takes factories or types, not
scripts; no `prefixId`; consuming from a Kamelet (`from("kamelet:x")`) and Kamelet YAML specs are not supported.

### Route configuration

Camel 3.12+'s `routeConfiguration`: error handler, `onException`, `onCompletion`, `intercept`, `interceptFrom` and `interceptSendToEndpoint`
shared by many routes. Defined with `RouteBuilder.RouteConfiguration([id,] c => c.ErrorHandler(..).OnException<T>(..).OnCompletion(..)
.Intercept(..).InterceptFrom([pattern,] ..).InterceptSendToEndpoint(..))`, usually in a `RouteConfigurationBuilder` subclass
(`Configuration()` instead of `Configure()`), or added with `context.AddRouteConfiguration(definition)`. Configurations are kept in the context
(`RouteConfigurationDefinitions`) and resolved when a route is *created* (`RouteContext.ApplyRouteConfigurations`, before the error handler
is chosen), so they may be added before or after the routes, as long as it is before the routes are created; the route definition itself is
not changed (so AdviceWith and restarts see the same definition).

- **Selection.** `.RouteConfigurationId("a, b*")` (`RouteDefinition.RouteConfigurationIds`): for each id or pattern in order, the
  configurations whose id matches (exact, wildcard, regular expression); an exact id matching nothing fails route creation (a typo would
  otherwise silently drop error handling). A route without ids uses the configurations without id (or with id `*`): the defaults.
- **Precedence** (`RouteContext.ErrorHandler`, `OnExceptions`, `OnCompletions`, `Intercepts`, `InterceptFroms`, `InterceptSendToEndpoints`,
  used by `CaravanContext.CreateRoute`, the error handler, `InterceptFromProcessor` and `OnCompletionProcessor`):
  - error handler: the route's own → its route builder's → the first selected configuration having one → `context.ErrorHandler` → default;
  - `onException`: the route's own (route scoped, always tried first), then its builder's, then the configurations' in selection order
    (global scope; among global clauses an exact type match wins, else the closest type, ties to the first);
  - `onCompletion`: the route's own, else its builder's, else those of all selected configurations;
  - intercepts of all three kinds: the route's (builder, advice) followed by the configurations'.

Differences from Camel: unknown exact ids fail route creation; there is no `precondition`.

### Global configuration

Context-wide defaults (Camel's `CamelContext`/main options), all on `ICaravanContext`; route options override them where both exist:

| Option | Default | Notes |
|---|---|---|
| `ErrorHandler` | null (= default error handler) | Overridden by route, builder and route configuration (see above). |
| `StreamCaching.Enabled` | true | `.StreamCaching(bool)` on a route overrides. |
| `AllowUseOriginalMessage` | false | Turned on automatically by `useOriginalMessage`. |
| `ShutdownStrategy.Timeout` | 45 s | Graceful shutdown. |
| `AutoStartup` | true | When false no route starts with the context (whatever its own `autoStartup`, as Camel); routes added to a running context follow their own option. |
| `RouteFilterIncludePattern` / `RouteFilterExcludePattern` | null | Comma-separated patterns (exact, wildcard, regex) matched against the route id and the `from` URI (`EndpointHelper.MatchEndpoint`) when routes are added; exclude wins; filtered routes are dropped (debug log). Handy to run a subset of routes in a test. |
| `UuidGenerator` | `DefaultUuidGenerator` | `IUuidGenerator.GenerateUuid()` makes exchange ids (also correlated copies), message ids, in-memory saga ids and `${uuid}`. Built-ins (`Caravan.Impl`): `DefaultUuidGenerator` (Guid, 32 hex), `RandomUuidGenerator` (Guid with dashes), `ShortUuidGenerator` (16 hex: random prefix + counter), `SimpleUuidGenerator` (1, 2, 3...; predictable ids for tests), `ClassicUuidGenerator` (`ID-host-millis-0-n`). Simple's `${uuid:default|random|short|simple|classic}` pick one explicitly. |

Message history and tracing come with observability (row 13).

### Endpoint URIs

- `scheme:path?k=v&k2=v2` (also `scheme://path`). Values may use `RAW(...)` to avoid decoding.
- `{{key}}` / `{{key:default}}` / `{{env:NAME}}` / `{{?key}}` placeholders are resolved by `ICaravanContext.PropertiesComponent`
  (`ParseUri`, see "Properties") when the endpoint is created.
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
- `RouteContext.RouteConfigurations` and the effective `OnExceptions`/`OnCompletions`/`Intercepts`/... – what a route uses once route
  configurations are applied; features building route-level processors read these rather than the `RouteDefinition` lists.
- `RouteDefinition.Extensions` – feature-specific per-route configuration keyed by type.
- `ICaravanContext.InterceptStrategies` / `RouteContext.InterceptStrategies` – wrap nodes.
- `SendToEndpointInterceptors.For(context)` – intercept every send to matching endpoints (`ISendToEndpointInterceptor`). Code that sends
  to endpoints creates producers with `SendToEndpointInterceptors.CreateProducer(endpoint)`, never `endpoint.CreateProducer()` directly.

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
coerce types like Camel (`Support.ObjectHelper`). String options that are dynamic in Camel are Simple expressions, compiled when
the route is built (`SimpleLanguage.Expression(text)`): `toD`, `wireTap`, `enrich`/`pollEnrich` URIs, `recipientList` and
`routingSlip` strings, `claimCheck` keys and `log` messages. Use `Expr.Simple`/`Pred.Simple` (or `Lang.Simple`) for expression and
predicate parameters; never parse `${...}` by hand.

## Adding a component

Derive from `ComponentBase` (override `CreateEndpoint(uri, remaining, parameters)`), `EndpointBase`
(public settable properties = URI options), `ProducerBase` and/or `ConsumerBase`. Consumers call
`Processor.ProcessAsync(exchange)` with exchanges created by `Endpoint.CreateExchange()`.
Register built-in components in the `CaravanContext` static constructor. Background consumers follow the rules in
"Consumers: lifecycle, suspension and shutdown" above.

Built-in components: `direct`, `seda` (and `vm`, its process-wide variant), `timer`, `scheduler`, `log`, `mock`, `saga`, `dataformat`, `bean`,
`class`, `controlbus`, `stub`, `ref`, `dataset`, `dataset-test`, `browse`, `kamelet`.

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
- File-scoped namespaces: `Caravan` (main API), `Caravan.Spi`, `Caravan.Model`, `Caravan.Builder`, `Caravan.Language` (languages;
  `Caravan.Language.Simple` holds the Simple parser),
  `Caravan.Processors`, `Caravan.Support`, `Caravan.Impl`, `Caravan.Aggregation` (built-in aggregation strategies),
  `Caravan.LoadBalancing` (built-in load balancers), `Caravan.Idempotent` (idempotent repositories), `Caravan.Saga` (saga SPI and in-memory service),
  `Caravan.DataFormat` (built-in data formats and their registry), `Caravan.Streams` (stream caching),
  `Caravan.Components.<Name>`; `Caravan.Testing` (the test kit project).
- Tests: xUnit (`tests/Caravan.Core.Tests`, `tests/Caravan.Testing.Tests`), one test class per feature, descriptive `Snake_case_names`. No sleeps where a mock
  expectation or `TimeProvider` can be used instead; keep the suite fast.
