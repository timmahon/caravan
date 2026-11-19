using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// Wraps the nodes of one route with a <see cref="RedeliveryErrorHandler"/> configured from a <see cref="RedeliveryErrorHandlerDefinition"/>
/// and the route's <c>onException</c> clauses. Nodes of <c>doTry</c>/<c>doCatch</c>/<c>doFinally</c> and <c>onException</c> blocks, and the nodes
/// inside a <c>circuitBreaker</c> (unless it inherits the error handler), are not wrapped: they handle errors themselves (as in Camel).
/// </summary>
internal sealed class RedeliveryErrorHandlerFactory : IErrorHandlerFactory
{
    private readonly RouteContext _routeContext;
    private List<ExceptionPolicy>? _policies;
    private bool _building;

    public RedeliveryErrorHandlerFactory(RouteContext routeContext, RedeliveryErrorHandlerDefinition definition)
    {
        _routeContext = routeContext;
        Definition = definition;
        RedeliveryPolicy = definition.RedeliveryPolicy.CreatePolicy();
        if (RedeliveryPolicy.DelayPattern is { } pattern)
        {
            RedeliveryPolicy.ValidateDelayPattern(pattern);
        }

        if (definition.DeadLetterUri is { } uri)
        {
            var sender = new SendProcessor(routeContext.ResolveEndpoint(uri));
            routeContext.AddService(sender);
            DeadLetter = sender;
        }

        RedeliveryEnabled = RedeliveryPolicy.MaximumRedeliveries != 0 || definition.RetryWhile is not null ||
            routeContext.OnExceptions.Any(o => o.RedeliveryPolicy.EnablesRedelivery || o.RetryWhile is not null);
        LoggerName = definition.LoggerName ?? "Caravan.Processors." + definition.Name;
    }

    public RedeliveryErrorHandlerDefinition Definition { get; }

    public RedeliveryPolicy RedeliveryPolicy { get; }

    /// <summary>Sends to the dead letter endpoint (dead letter channel only).</summary>
    public IProcessor? DeadLetter { get; }

    /// <summary>Whether any policy can redeliver: when not, no copy of the message is kept before each node.</summary>
    public bool RedeliveryEnabled { get; }

    public string LoggerName { get; }

    public IReadOnlyList<ExceptionPolicy> Policies => _policies ?? [];

    public IProcessor Wrap(RouteContext routeContext, ProcessorDefinition definition, IProcessor target)
    {
        if (IsSelfHandling(definition))
        {
            return target;
        }

        EnsurePolicies();
        return new RedeliveryErrorHandler(this, definition, target);
    }

    public ExceptionPolicy? FindPolicy(Exchange exchange, Exception exception) => ExceptionPolicyStrategy.Find(Policies, exchange, exception);

    private static bool IsSelfHandling(ProcessorDefinition definition)
    {
        for (var node = definition; node is not null; node = node.Parent)
        {
            if (node is TryDefinition or CatchDefinition or FinallyDefinition or OnExceptionDefinition)
            {
                return true;
            }

            if (node.Parent is CircuitBreakerDefinition { InheritErrorHandler: false })
            {
                return true;
            }
        }

        return false;
    }

    private void EnsurePolicies()
    {
        if (_policies is not null || _building)
        {
            return;
        }

        // Built on first use, when the route's configurers have run, so the blocks get the same intercept strategies as the route.
        _building = true;
        var policies = new List<ExceptionPolicy>();
        foreach (var onException in _routeContext.OnExceptions)
        {
            onException.Id ??= _routeContext.Context.NextNodeId(onException.ShortName);
            var policy = onException.RedeliveryPolicy.CreatePolicy(RedeliveryPolicy);
            if (policy.DelayPattern is { } pattern)
            {
                RedeliveryPolicy.ValidateDelayPattern(pattern);
            }

            var processor = onException.Outputs.Count > 0 ? onException.CreateProcessor(_routeContext) : null;
            if (processor is not null)
            {
                _routeContext.AddService(processor);
            }

            policies.Add(new ExceptionPolicy(onException, policy, processor));
        }

        _policies = policies;
        _building = false;
    }
}
