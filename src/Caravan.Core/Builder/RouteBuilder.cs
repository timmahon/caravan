using Caravan.Model;

namespace Caravan.Builder;

/// <summary>Something that adds routes to a context.</summary>
public interface IRoutesBuilder
{
    /// <summary>Builds the route definitions. Called once when the routes are added to <paramref name="context"/>.</summary>
    IReadOnlyList<RouteDefinition> Build(ICaravanContext context);
}

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

    public IReadOnlyList<RouteDefinition> Build(ICaravanContext context)
    {
        _context = context;
        try
        {
            _routes.Clear();
            Configure();
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

    /// <summary>Creates a routes builder from a lambda.</summary>
    public static IRoutesBuilder Create(Action<RouteBuilder> configure) => new LambdaRouteBuilder(configure);

    private sealed class LambdaRouteBuilder(Action<RouteBuilder> configure) : RouteBuilder
    {
        public override void Configure() => configure(this);
    }
}

/// <summary>
/// Hooks invoked after a <see cref="RouteBuilder"/> has configured its routes. Features such as
/// route-scoped configuration use this to post-process definitions.
/// </summary>
public sealed class RouteBuilderHooks
{
    public List<Action<RouteBuilder, List<RouteDefinition>>> Hooks { get; } = [];
}
