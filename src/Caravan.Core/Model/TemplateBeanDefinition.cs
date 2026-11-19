namespace Caravan.Model;

/// <summary>
/// A bean local to each route created from a template (Camel's <c>templateBean</c>): created per route by <see cref="Factory"/> and bound in
/// the route's <see cref="Impl.LocalBeanRegistry"/>.
/// </summary>
public sealed class TemplateBeanDefinition
{
    /// <summary>Creates a template bean.</summary>
    /// <param name="name">The bean name.</param>
    /// <param name="factory">Creates the bean for a route.</param>
    /// <param name="beanType">The bean type, when known (informational).</param>
    public TemplateBeanDefinition(string name, Func<RouteTemplateContext, object> factory, Type? beanType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        Name = name;
        Factory = factory;
        BeanType = beanType;
    }

    /// <summary>The bean name.</summary>
    public string Name { get; }

    /// <summary>Creates the bean for a route (receives the route's parameters).</summary>
    public Func<RouteTemplateContext, object> Factory { get; }

    /// <summary>The bean type, when known.</summary>
    public Type? BeanType { get; }

    /// <inheritdoc />
    public override string ToString() => BeanType is null ? Name : $"{Name}({BeanType.Name})";
}
