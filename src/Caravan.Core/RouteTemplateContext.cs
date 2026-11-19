using Caravan.Impl;

namespace Caravan;

/// <summary>
/// The parameters and local beans of one route being created from a template (Camel's <c>RouteTemplateContext</c>). Template bean factories and
/// configurers receive it; <see cref="ICaravanContext.AddRouteFromTemplateAsync(string?, string, RouteTemplateContext, CancellationToken)"/>
/// takes one to pass beans as well as parameters.
/// </summary>
public sealed class RouteTemplateContext
{
    /// <summary>Creates an empty template context.</summary>
    /// <param name="context">The context the route is added to.</param>
    public RouteTemplateContext(ICaravanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    /// <summary>The context the route is added to.</summary>
    public ICaravanContext Context { get; }

    /// <summary>The id of the template, once the route is being created.</summary>
    public string? TemplateId { get; internal set; }

    /// <summary>The id of the route, once it is known (before the beans are created).</summary>
    public string? RouteId { get; internal set; }

    /// <summary>The parameter values by name (case-insensitive); defaults are added when the route is created.</summary>
    public IDictionary<string, object?> Parameters { get; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The route's local beans.</summary>
    public LocalBeanRegistry LocalBeans { get; } = new();

    /// <summary>Called before the template's beans are created, after the template's own configurer.</summary>
    public Action<RouteTemplateContext>? Configurer { get; set; }

    /// <summary>Whether the route is created by the <c>kamelet</c> component.</summary>
    internal bool IsKamelet { get; set; }

    /// <summary>Changes the route's copy of the template's route before it is added.</summary>
    internal Action<Model.RouteDefinition>? Handler { get; set; }

    /// <summary>Beans created for the route once its id is known, after the template's beans (same name: these win).</summary>
    internal List<Model.TemplateBeanDefinition> Beans { get; } = [];

    /// <summary>Sets a parameter.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This context.</returns>
    public RouteTemplateContext SetParameter(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Parameters[name] = value;
        return this;
    }

    /// <summary>Whether the parameter has a (non-null) value.</summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>Whether it has a value.</returns>
    public bool HasParameter(string name) => Parameters.TryGetValue(name, out var value) && value is not null;

    /// <summary>The value of a parameter, with the context's placeholders resolved in text values; null when it has none.</summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>The value.</returns>
    public object? GetProperty(string name) => Parameters.TryGetValue(name, out var value)
        ? value is string text ? Context.PropertiesComponent.ResolvePlaceholders(text) : value
        : null;

    /// <summary>The value of a parameter converted to <typeparamref name="T"/> with the context's type converters.</summary>
    /// <param name="name">The parameter name.</param>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>The value, or the default of <typeparamref name="T"/> when there is none.</returns>
    public T? GetProperty<T>(string name) => GetProperty(name) is { } value ? (T?)Context.TypeConverter.Convert(typeof(T), null, value) : default;

    /// <summary>Binds a local bean of the route.</summary>
    /// <param name="name">The bean name.</param>
    /// <param name="bean">The bean.</param>
    /// <returns>This context.</returns>
    public RouteTemplateContext Bind(string name, object bean)
    {
        LocalBeans.Bind(name, bean);
        return this;
    }
}
