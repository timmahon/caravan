using Caravan.Model;

namespace Caravan.Builder;

/// <summary>Configures a <see cref="TemplatedRouteDefinition"/> declared with <see cref="RouteBuilder.TemplatedRoute"/>.</summary>
public sealed class TemplatedRouteDefinitionBuilder
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The templated route.</param>
    public TemplatedRouteDefinitionBuilder(TemplatedRouteDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }

    /// <summary>The templated route.</summary>
    public TemplatedRouteDefinition Definition { get; }

    /// <summary>Sets the route id.</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteDefinitionBuilder RouteId(string routeId)
    {
        Definition.RouteId = routeId;
        return this;
    }

    /// <summary>Sets a parameter.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteDefinitionBuilder Parameter(string name, object? value)
    {
        Definition.Parameters[name] = value;
        return this;
    }

    /// <summary>Binds <paramref name="bean"/> as a local bean of the route (replacing a template bean of the same name).</summary>
    /// <param name="name">The bean name.</param>
    /// <param name="bean">The bean.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteDefinitionBuilder Bean(string name, object bean)
    {
        ArgumentNullException.ThrowIfNull(bean);
        Definition.Beans.Add(new TemplateBeanDefinition(name, _ => bean, bean.GetType()));
        return this;
    }

    /// <summary>Adds a local bean of the route created by <paramref name="factory"/>.</summary>
    /// <param name="name">The bean name.</param>
    /// <param name="factory">Creates the bean.</param>
    /// <typeparam name="T">The bean type.</typeparam>
    /// <returns>This builder.</returns>
    public TemplatedRouteDefinitionBuilder Bean<T>(string name, Func<RouteTemplateContext, T> factory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        Definition.Beans.Add(new TemplateBeanDefinition(name, rtc => factory(rtc), typeof(T)));
        return this;
    }

    /// <summary>Changes the route's copy of the template's route before it is added.</summary>
    /// <param name="handler">The callback.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteDefinitionBuilder Handler(Action<RouteDefinition> handler)
    {
        Definition.Handler = handler;
        return this;
    }

    /// <summary>Sets a callback run before the route's beans are created.</summary>
    /// <param name="configurer">The callback.</param>
    /// <returns>This builder.</returns>
    public TemplatedRouteDefinitionBuilder Configure(Action<RouteTemplateContext> configurer)
    {
        Definition.Configurer = configurer;
        return this;
    }
}
