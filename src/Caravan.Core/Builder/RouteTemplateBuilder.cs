using Caravan.Components.Bean;
using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Declares a route template in a <see cref="RouteBuilder"/> (Camel's <c>routeTemplate</c>):
/// <code>
/// RouteTemplate("timerTemplate")
///     .TemplateParameter("name")
///     .TemplateParameter("period", "1000")
///     .TemplateBean("greeter", rtc =&gt; new Greeter(rtc.GetProperty&lt;string&gt;("name")))
///     .From("timer:{{name}}?period={{period}}")
///     .Bean("greeter")
///     .To("mock:{{name}}");
/// </code>
/// </summary>
public sealed class RouteTemplateBuilder
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The template being built.</param>
    public RouteTemplateBuilder(RouteTemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }

    /// <summary>The template being built.</summary>
    public RouteTemplateDefinition Definition { get; }

    /// <summary>Adds a parameter, required unless it has a default value.</summary>
    /// <param name="name">The parameter name, referenced as <c>{{name}}</c>.</param>
    /// <param name="defaultValue">The default value, or null.</param>
    /// <param name="description">A description.</param>
    /// <param name="required">Whether a value must be given when there is no default (default true).</param>
    /// <returns>This builder.</returns>
    public RouteTemplateBuilder TemplateParameter(string name, string? defaultValue = null, string? description = null, bool required = true)
    {
        Definition.Parameters.Add(new RouteTemplateParameterDefinition(name)
        {
            DefaultValue = defaultValue,
            Description = description,
            Required = required && defaultValue is null,
        });
        return this;
    }

    /// <summary>Adds an optional parameter without default (Camel's <c>templateOptionalParameter</c>).</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="description">A description.</param>
    /// <returns>This builder.</returns>
    public RouteTemplateBuilder TemplateOptionalParameter(string name, string? description = null) =>
        TemplateParameter(name, null, description, required: false);

    /// <summary>Adds a bean created for each route by <paramref name="factory"/> and bound in the route's local beans.</summary>
    /// <param name="name">The bean name.</param>
    /// <param name="factory">Creates the bean from the route's parameters.</param>
    /// <typeparam name="T">The bean type.</typeparam>
    /// <returns>This builder.</returns>
    public RouteTemplateBuilder TemplateBean<T>(string name, Func<RouteTemplateContext, T> factory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        Definition.Beans.Add(new TemplateBeanDefinition(name, rtc => factory(rtc), typeof(T)));
        return this;
    }

    /// <summary>
    /// Adds a bean of type <paramref name="type"/> created for each route (from the context's service provider, else a constructor whose
    /// parameters it can supply) and bound in the route's local beans.
    /// </summary>
    /// <param name="name">The bean name.</param>
    /// <param name="type">The bean type.</param>
    /// <returns>This builder.</returns>
    public RouteTemplateBuilder TemplateBean(string name, Type type)
    {
        Definition.Beans.Add(CreateTypeBean(name, type));
        return this;
    }

    /// <summary>Sets the template's description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>This builder.</returns>
    public RouteTemplateBuilder TemplateDescription(string description)
    {
        Definition.Description = description;
        return this;
    }

    /// <summary>Sets a callback run for each route before its beans are created (Camel's <c>configure</c>), e.g. to compute parameters.</summary>
    /// <param name="configurer">The callback.</param>
    /// <returns>This builder.</returns>
    public RouteTemplateBuilder Configure(Action<RouteTemplateContext> configurer)
    {
        Definition.Configurer = configurer;
        return this;
    }

    /// <summary>Defines the template's route, consuming from <paramref name="uri"/> (placeholders allowed).</summary>
    /// <param name="uri">The endpoint URI.</param>
    /// <returns>The route builder.</returns>
    public RouteDefinitionBuilder From(string uri)
    {
        var route = new RouteDefinition(uri);
        Definition.Route = route;
        return new RouteDefinitionBuilder(route);
    }

    /// <summary>A template bean created from <paramref name="type"/>.</summary>
    internal static TemplateBeanDefinition CreateTypeBean(string name, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new TemplateBeanDefinition(
            name,
            rtc => BeanActivator.Create(rtc.Context, type) ?? throw new InvalidOperationException($"Cannot create a bean of the static class {type.FullName}"),
            type);
    }
}
