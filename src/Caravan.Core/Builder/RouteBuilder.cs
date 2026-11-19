using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Base class for defining routes in C# (Camel's <c>RouteBuilder</c>):
/// <code>
/// public sealed class OrderRoutes : RouteBuilder
/// {
///     public override void Configure()
///     {
///         From("direct:orders")
///             .RouteId("orders")
///             .SetHeader("received", Expr.Constant(true))
///             .To("log:orders")
///             .To("mock:result");
///     }
/// }
/// </code>
/// </summary>
public abstract class RouteBuilder : IRoutesBuilder
{
    private readonly List<RouteDefinition> _routes = [];
    private readonly List<OnExceptionDefinition> _onExceptions = [];
    private readonly List<OnCompletionDefinition> _onCompletions = [];
    private ErrorHandlerDefinition? _errorHandler;
    private ICaravanContext? _context;

    /// <summary>The context the routes are being added to (available inside <see cref="Configure"/>).</summary>
    protected ICaravanContext Context =>
        _context ?? throw new InvalidOperationException("Context is only available while Configure() runs");

    /// <summary>Defines routes using <see cref="From"/>.</summary>
    public abstract void Configure();

    /// <summary>Starts a new route consuming from <paramref name="uri"/>.</summary>
    public RouteDefinitionBuilder From(string uri)
    {
        var route = new RouteDefinition(uri);
        _routes.Add(route);
        return new RouteDefinitionBuilder(route);
    }

    /// <summary>Sets the error handler of this builder's routes (a route's own error handler takes precedence).</summary>
    public void ErrorHandler(ErrorHandlerDefinition errorHandler)
    {
        ArgumentNullException.ThrowIfNull(errorHandler);
        _errorHandler = errorHandler;
    }

    /// <summary>Creates a default error handler (no redelivery unless configured; failures are logged and propagate).</summary>
    public DefaultErrorHandlerDefinition DefaultErrorHandler() => new();

    /// <summary>Creates a dead letter channel sending exhausted failures to <paramref name="deadLetterUri"/>.</summary>
    public DeadLetterChannelDefinition DeadLetterChannel(string deadLetterUri) => new(deadLetterUri);

    /// <summary>Creates a "no error handler": failures propagate as they are and <c>onException</c> does not apply.</summary>
    public NoErrorHandlerDefinition NoErrorHandler() => new();

    /// <summary>
    /// Adds an <c>onException</c> clause for <typeparamref name="TException"/> applying to all routes of this builder
    /// (route-scoped clauses take precedence): <c>OnException&lt;IOException&gt;(o =&gt; o.Handled(true).To("mock:error"))</c>.
    /// </summary>
    public OnExceptionDefinition OnException<TException>(Action<OnExceptionBuilder>? configure = null)
        where TException : Exception => OnException([typeof(TException)], configure);

    /// <summary>Adds an <c>onException</c> clause for <paramref name="exceptionType"/> applying to all routes of this builder.</summary>
    public OnExceptionDefinition OnException(Type exceptionType, Action<OnExceptionBuilder>? configure = null) =>
        OnException([exceptionType], configure);

    /// <summary>Adds an <c>onException</c> clause for any of <paramref name="exceptionTypes"/> applying to all routes of this builder.</summary>
    public OnExceptionDefinition OnException(Type[] exceptionTypes, Action<OnExceptionBuilder>? configure = null)
    {
        var definition = new OnExceptionDefinition(exceptionTypes);
        configure?.Invoke(new OnExceptionBuilder(definition));
        _onExceptions.Add(definition);
        return definition;
    }

    /// <summary>
    /// Adds an <c>onCompletion</c> block applying to the routes of this builder that have none of their own:
    /// <c>OnCompletion(c =&gt; c.OnFailureOnly().To("mock:failed"))</c>.
    /// </summary>
    public OnCompletionDefinition OnCompletion(Action<OnCompletionBuilder> configure)
    {
        var definition = new OnCompletionDefinition();
        configure(new OnCompletionBuilder(definition));
        _onCompletions.Add(definition);
        return definition;
    }

    /// <inheritdoc />
    public IReadOnlyList<RouteDefinition> Build(ICaravanContext context)
    {
        _context = context;
        try
        {
            _routes.Clear();
            _onExceptions.Clear();
            _onCompletions.Clear();
            _errorHandler = null;
            Configure();
            ApplyBuilderConfiguration();
            foreach (var hook in context.GetExtension<RouteBuilderHooks>()?.Hooks ?? [])
            {
                hook(this, _routes);
            }

            return [.. _routes];
        }
        finally
        {
            _context = null;
        }
    }

    private void ApplyBuilderConfiguration()
    {
        foreach (var route in _routes)
        {
            route.ErrorHandler ??= _errorHandler;
            route.OnExceptions.AddRange(_onExceptions.Where(o => !route.OnExceptions.Contains(o)));
            if (route.OnCompletions.Count == 0)
            {
                route.OnCompletions.AddRange(_onCompletions);
            }
        }
    }

    /// <summary>Creates a routes builder from a lambda.</summary>
    public static IRoutesBuilder Create(Action<RouteBuilder> configure) => new LambdaRouteBuilder(configure);

    private sealed class LambdaRouteBuilder(Action<RouteBuilder> configure) : RouteBuilder
    {
        public override void Configure() => configure(this);
    }
}
