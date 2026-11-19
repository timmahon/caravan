using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Creates a route from a template in application code (Camel's <c>TemplatedRouteBuilder</c>):
/// <code>
/// var routeId = await TemplatedRouteBuilder.Builder(context, "timerTemplate")
///     .RouteId("fast")
///     .Parameter("name", "fast")
///     .Parameter("period", 100)
///     .Bean("greeter", new Greeter("hi"))
///     .AddAsync();
/// </code>
/// </summary>
public sealed class TemplatedRouteBuilder
{
    private readonly ICaravanContext _context;
    private readonly TemplatedRouteDefinition _definition;

    private TemplatedRouteBuilder(ICaravanContext context, string templateId)
    {
        _context = context;
        _definition = new TemplatedRouteDefinition(templateId);
    }

    /// <summary>Starts a builder for a route from the template <paramref name="templateId"/> of <paramref name="context"/>.</summary>
    /// <param name="context">The context holding the template.</param>
    /// <param name="templateId">The template id.</param>
    /// <returns>The builder.</returns>
    public static TemplatedRouteBuilder Builder(ICaravanContext context, string templateId)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TemplatedRouteBuilder(context, templateId);
    }

    /// <summary>Sets the route id.</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteBuilder RouteId(string routeId)
    {
        _definition.RouteId = routeId;
        return this;
    }

    /// <summary>Sets a parameter.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteBuilder Parameter(string name, object? value)
    {
        _definition.Parameters[name] = value;
        return this;
    }

    /// <summary>Sets parameters.</summary>
    /// <param name="parameters">The parameters.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteBuilder Parameters(IEnumerable<KeyValuePair<string, object?>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        foreach (var (name, value) in parameters)
        {
            _definition.Parameters[name] = value;
        }

        return this;
    }

    /// <summary>Binds <paramref name="bean"/> as a local bean of the route (replacing a template bean of the same name).</summary>
    /// <param name="name">The bean name.</param>
    /// <param name="bean">The bean.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteBuilder Bean(string name, object bean)
    {
        ArgumentNullException.ThrowIfNull(bean);
        _definition.Beans.Add(new TemplateBeanDefinition(name, _ => bean, bean.GetType()));
        return this;
    }

    /// <summary>Adds a local bean of the route created by <paramref name="factory"/>.</summary>
    /// <param name="name">The bean name.</param>
    /// <param name="factory">Creates the bean.</param>
    /// <typeparam name="T">The bean type.</typeparam>
    /// <returns>This builder.</returns>
    public TemplatedRouteBuilder Bean<T>(string name, Func<RouteTemplateContext, T> factory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        _definition.Beans.Add(new TemplateBeanDefinition(name, rtc => factory(rtc), typeof(T)));
        return this;
    }

    /// <summary>Changes the route's copy of the template's route before it is added (Camel's <c>handler</c>).</summary>
    /// <param name="handler">The callback.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteBuilder Handler(Action<RouteDefinition> handler)
    {
        _definition.Handler = handler;
        return this;
    }

    /// <summary>Sets a callback run before the route's beans are created (Camel's <c>configure</c>).</summary>
    /// <param name="configurer">The callback.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteBuilder Configure(Action<RouteTemplateContext> configurer)
    {
        _definition.Configurer = configurer;
        return this;
    }

    /// <summary>Creates the route (and starts it when the context is running).</summary>
    /// <param name="cancellationToken">Cancels the start of the route.</param>
    /// <returns>The id of the route.</returns>
    /// <exception cref="FailedToCreateRouteFromTemplateException">The route cannot be created.</exception>
    public Task<string> AddAsync(CancellationToken cancellationToken = default) =>
        _context.AddRouteFromTemplateAsync(_definition.RouteId, _definition.TemplateId, _definition.CreateTemplateContext(_context), cancellationToken);
}
