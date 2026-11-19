using Caravan.Spi;
using Caravan.Testing.Notify;

namespace Caravan.Testing;

/// <summary>
/// Waits until routes have processed some messages, without changing the routes (Camel's <c>NotifyBuilder</c>), e.g. to test routes fed by
/// <c>seda</c>, timers or other asynchronous consumers:
/// <code>
/// using var notify = new NotifyBuilder(context).FromRoute("orders").WhenDone(5).Create();
/// // ... send or wait for messages ...
/// Assert.True(await notify.MatchesAsync(TimeSpan.FromSeconds(10)));
/// </code>
/// Conditions (<c>When...</c>) are combined in groups with <see cref="And"/>, <see cref="Or"/> and <see cref="Not"/>; within a group all
/// must hold. Filters (<see cref="From"/>, <see cref="FromRoute"/>, <see cref="Filter(IPredicate)"/>, <see cref="WereSentTo"/>) restrict the
/// exchanges seen by the conditions that follow them in the group (<see cref="WereSentTo"/> applies to the whole group). It listens to the
/// context's events (<see cref="IEventNotifier"/>): exchanges count when a route creates their unit of work (received) and when that unit of
/// work is done (done = completed or failed), so an exchange passing through <c>direct</c> routes counts once and an exchange handed to a
/// <c>seda</c> route counts again there. <see cref="Create"/> starts listening; dispose the builder to stop.
/// </summary>
public sealed class NotifyBuilder : IDisposable
{
    private readonly ICaravanContext _context;
    private readonly Lock _lock = new();
    private readonly List<(NotifyOperation Operation, List<NotifyEventPredicate> Predicates)> _groups = [];
    private readonly Notifier _notifier;
    private List<NotifyEventPredicate> _current = [];
    private int _wereSentToIndex;
    private bool _created;
    private TaskCompletionSource _matched = NewSignal();

    /// <summary>Creates a builder for <paramref name="context"/>; add conditions, then call <see cref="Create"/>.</summary>
    /// <param name="context">The context to watch.</param>
    public NotifyBuilder(ICaravanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _notifier = new Notifier(this);
        _groups.Add((NotifyOperation.And, _current));
    }

    /// <summary>Only exchanges that entered through an endpoint matching <paramref name="uriPattern"/> (exact, wildcard or regular expression).</summary>
    /// <param name="uriPattern">The endpoint URI pattern.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder From(string uriPattern) => Add(new FromEndpointNotifyFilter(_context, uriPattern));

    /// <summary>Only exchanges created by a route whose id matches <paramref name="routeIdPattern"/> (exact, wildcard or regular expression).</summary>
    /// <param name="routeIdPattern">The route id pattern.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder FromRoute(string routeIdPattern) => Add(new FromRouteNotifyFilter(routeIdPattern));

    /// <summary>Only events of exchanges matching <paramref name="predicate"/> (evaluated at each event).</summary>
    /// <param name="predicate">The filter.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder Filter(IPredicate predicate) => Add(new ExchangeFilterNotifyPredicate(predicate));

    /// <summary>Only events of exchanges matching <paramref name="predicate"/> (evaluated at each event).</summary>
    /// <param name="predicate">The filter.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder Filter(Func<Exchange, bool> predicate) => Filter(Pred.From(predicate));

    /// <summary>Only exchanges that were sent to an endpoint matching <paramref name="uriPattern"/> (applies to the whole group).</summary>
    /// <param name="uriPattern">The endpoint URI pattern.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WereSentTo(string uriPattern)
    {
        EnsureNotCreated();
        _current.Insert(_wereSentToIndex++, new SentToNotifyFilter(_context, uriPattern));
        return this;
    }

    /// <summary>At least <paramref name="number"/> exchanges were received (created by a route).</summary>
    /// <param name="number">The count.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenReceived(int number) => Add(new CountNotifyPredicate(NotifyCountKind.Received, number, exact: false));

    /// <summary>At least <paramref name="number"/> exchanges are done (completed or failed).</summary>
    /// <param name="number">The count.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenDone(int number) => Add(new CountNotifyPredicate(NotifyCountKind.Done, number, exact: false));

    /// <summary>At least <paramref name="number"/> exchanges completed successfully.</summary>
    /// <param name="number">The count.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenCompleted(int number) => Add(new CountNotifyPredicate(NotifyCountKind.Completed, number, exact: false));

    /// <summary>At least <paramref name="number"/> exchanges failed.</summary>
    /// <param name="number">The count.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenFailed(int number) => Add(new CountNotifyPredicate(NotifyCountKind.Failed, number, exact: false));

    /// <summary>Exactly <paramref name="number"/> exchanges are done (completed or failed).</summary>
    /// <param name="number">The count.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenExactlyDone(int number) => Add(new CountNotifyPredicate(NotifyCountKind.Done, number, exact: true));

    /// <summary>Exactly <paramref name="number"/> exchanges completed successfully.</summary>
    /// <param name="number">The count.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenExactlyCompleted(int number) => Add(new CountNotifyPredicate(NotifyCountKind.Completed, number, exact: true));

    /// <summary>Exactly <paramref name="number"/> exchanges failed.</summary>
    /// <param name="number">The count.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenExactlyFailed(int number) => Add(new CountNotifyPredicate(NotifyCountKind.Failed, number, exact: true));

    /// <summary>The first received exchanges have these bodies, in order (more may follow).</summary>
    /// <param name="bodies">The expected bodies.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenBodiesReceived(params object?[] bodies) => Add(new BodiesNotifyPredicate(bodies, received: true, exact: false));

    /// <summary>Exactly these bodies were received, in order.</summary>
    /// <param name="bodies">The expected bodies.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenExactBodiesReceived(params object?[] bodies) => Add(new BodiesNotifyPredicate(bodies, received: true, exact: true));

    /// <summary>The first done exchanges have these bodies (as they were when done), in order (more may follow).</summary>
    /// <param name="bodies">The expected bodies.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenBodiesDone(params object?[] bodies) => Add(new BodiesNotifyPredicate(bodies, received: false, exact: false));

    /// <summary>Exactly these bodies are done, in order.</summary>
    /// <param name="bodies">The expected bodies.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenExactBodiesDone(params object?[] bodies) => Add(new BodiesNotifyPredicate(bodies, received: false, exact: true));

    /// <summary>Any received exchange matched <paramref name="predicate"/> (on arrival).</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenAnyReceivedMatches(IPredicate predicate) => Add(new ExchangeMatchesNotifyPredicate(predicate, received: true, all: false));

    /// <summary>Any received exchange matched <paramref name="predicate"/> (on arrival).</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenAnyReceivedMatches(Func<Exchange, bool> predicate) => WhenAnyReceivedMatches(Pred.From(predicate));

    /// <summary>At least one exchange was received and all received exchanges matched <paramref name="predicate"/> (on arrival).</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenAllReceivedMatches(IPredicate predicate) => Add(new ExchangeMatchesNotifyPredicate(predicate, received: true, all: true));

    /// <summary>At least one exchange was received and all received exchanges matched <paramref name="predicate"/> (on arrival).</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenAllReceivedMatches(Func<Exchange, bool> predicate) => WhenAllReceivedMatches(Pred.From(predicate));

    /// <summary>Any done exchange matched <paramref name="predicate"/> (when done).</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenAnyDoneMatches(IPredicate predicate) => Add(new ExchangeMatchesNotifyPredicate(predicate, received: false, all: false));

    /// <summary>Any done exchange matched <paramref name="predicate"/> (when done).</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenAnyDoneMatches(Func<Exchange, bool> predicate) => WhenAnyDoneMatches(Pred.From(predicate));

    /// <summary>At least one exchange is done and all done exchanges matched <paramref name="predicate"/> (when done).</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenAllDoneMatches(IPredicate predicate) => Add(new ExchangeMatchesNotifyPredicate(predicate, received: false, all: true));

    /// <summary>At least one exchange is done and all done exchanges matched <paramref name="predicate"/> (when done).</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder.</returns>
    public NotifyBuilder WhenAllDoneMatches(Func<Exchange, bool> predicate) => WhenAllDoneMatches(Pred.From(predicate));

    /// <summary>Starts a new group of predicates that must hold as well as the groups before.</summary>
    /// <returns>This builder.</returns>
    public NotifyBuilder And() => NewGroup(NotifyOperation.And);

    /// <summary>Starts a new group of predicates: the result holds when the groups before hold or this one does.</summary>
    /// <returns>This builder.</returns>
    public NotifyBuilder Or() => NewGroup(NotifyOperation.Or);

    /// <summary>Starts a new group of predicates that must not hold (while the groups before must).</summary>
    /// <returns>This builder.</returns>
    public NotifyBuilder Not() => NewGroup(NotifyOperation.Not);

    /// <summary>Starts listening to the context's events. Conditions cannot be added afterwards.</summary>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">No condition was added, or a group has no condition.</exception>
    public NotifyBuilder Create()
    {
        lock (_lock)
        {
            EnsureNotCreated();
            if (_groups.Any(g => !g.Predicates.Any(p => !p.IsFilter)))
            {
                throw new InvalidOperationException("NotifyBuilder must contain at least one condition (When...) in each group");
            }

            _created = true;
            UpdateMatched();
        }

        _context.EventNotifiers.Add(_notifier);
        return this;
    }

    /// <summary>Whether the conditions hold now.</summary>
    /// <returns>Whether they hold.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Create"/> was not called.</exception>
    public bool Matches()
    {
        lock (_lock)
        {
            EnsureCreated();
            return Evaluate();
        }
    }

    /// <summary>
    /// Waits (in real time) until the conditions hold or <paramref name="timeout"/> elapses, then returns whether they hold. With conditions
    /// such as <see cref="WhenExactlyDone"/>, it returns as soon as they hold; check again later to make sure nothing more arrived.
    /// </summary>
    /// <param name="timeout">The longest to wait.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>Whether the conditions hold.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Create"/> was not called.</exception>
    public async Task<bool> MatchesAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Task matched;
        lock (_lock)
        {
            EnsureCreated();
            if (Evaluate())
            {
                return true;
            }

            matched = _matched.Task;
        }

        try
        {
            await matched.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Report the current state below.
        }

        return Matches();
    }

    /// <summary>Forgets everything seen so far (the builder keeps listening).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            foreach (var predicate in _groups.SelectMany(g => g.Predicates))
            {
                predicate.Reset();
            }

            if (_created)
            {
                UpdateMatched();
            }
        }
    }

    /// <summary>Stops listening to the context's events.</summary>
    public void Dispose() => _context.EventNotifiers.Remove(_notifier);

    /// <inheritdoc />
    public override string ToString()
    {
        lock (_lock)
        {
            return string.Join(
                " ",
                _groups.Select((g, i) => (i == 0 ? string.Empty : g.Operation.ToString().ToLowerInvariant() + " ") + string.Join(".", g.Predicates)));
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private NotifyBuilder Add(NotifyEventPredicate predicate)
    {
        lock (_lock)
        {
            EnsureNotCreated();
            _current.Add(predicate);
        }

        return this;
    }

    private NotifyBuilder NewGroup(NotifyOperation operation)
    {
        lock (_lock)
        {
            EnsureNotCreated();
            _current = [];
            _wereSentToIndex = 0;
            _groups.Add((operation, _current));
        }

        return this;
    }

    private void EnsureNotCreated()
    {
        if (_created)
        {
            throw new InvalidOperationException("NotifyBuilder has already been created");
        }
    }

    private void EnsureCreated()
    {
        if (!_created)
        {
            throw new InvalidOperationException("NotifyBuilder has not been created: call Create() first");
        }
    }

    private bool Evaluate()
    {
        var result = false;
        for (var i = 0; i < _groups.Count; i++)
        {
            var (operation, predicates) = _groups[i];
            var group = predicates.All(p => p.Matches());
            result = i == 0 ? group : operation switch
            {
                NotifyOperation.Or => result || group,
                NotifyOperation.Not => result && !group,
                _ => result && group,
            };
        }

        return result;
    }

    private void UpdateMatched()
    {
        if (Evaluate())
        {
            _matched.TrySetResult();
        }
        else if (_matched.Task.IsCompleted)
        {
            _matched = NewSignal();
        }
    }

    private void OnEvent(CaravanEvent evt)
    {
        if (evt.Exchange is not { } exchange)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var (_, predicates) in _groups)
            {
                foreach (var predicate in predicates)
                {
                    var pass = evt.Type switch
                    {
                        CaravanEventType.ExchangeCreated => predicate.OnCreated(exchange, evt),
                        CaravanEventType.ExchangeCompleted => predicate.OnCompleted(exchange, evt),
                        CaravanEventType.ExchangeFailed => predicate.OnFailed(exchange, evt),
                        _ => predicate.OnSent(exchange, evt),
                    };
                    if (!pass)
                    {
                        break;
                    }
                }
            }

            UpdateMatched();
        }
    }

    private sealed class Notifier(NotifyBuilder owner) : IEventNotifier
    {
        public bool IsEnabled(CaravanEvent evt) =>
            evt.Type is CaravanEventType.ExchangeCreated or CaravanEventType.ExchangeCompleted or CaravanEventType.ExchangeFailed
                or CaravanEventType.ExchangeSent;

        public void Notify(CaravanEvent evt) => owner.OnEvent(evt);
    }
}
