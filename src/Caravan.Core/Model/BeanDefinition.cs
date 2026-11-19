using Caravan.Components.Bean;
using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// Calls a bean method (<c>bean</c>); see <see cref="BeanProcessor"/>. The bean is <see cref="Bean"/>, an instance of <see cref="BeanType"/>
/// (from the service provider or created) or the registry bean <see cref="Ref"/>. A singleton registry bean is looked up, and a given method
/// name checked, when the route is created.
/// </summary>
public sealed class BeanDefinition : ProcessorDefinition
{
    /// <summary>The bean instance.</summary>
    public object? Bean { get; set; }

    /// <summary>The bean type, instantiated according to <see cref="Scope"/>.</summary>
    public Type? BeanType { get; set; }

    /// <summary>The name of a registry bean.</summary>
    public string? Ref { get; set; }

    /// <summary>The method specification: <c>name</c>, <c>name(string, int)</c> or <c>name(${body}, 'x')</c>; null chooses by the body.</summary>
    public string? Method { get; set; }

    /// <summary>How long the bean is kept (default <see cref="BeanScope.Singleton"/>).</summary>
    public BeanScope Scope { get; set; } = BeanScope.Singleton;

    /// <inheritdoc />
    public override string ShortName => "bean";

    /// <inheritdoc />
    public override string Label
    {
        get
        {
            var bean = Ref ?? BeanType?.Name ?? Bean?.GetType().Name;
            return Method is null ? $"bean[{bean}]" : $"bean[{bean}?method={Method}]";
        }
    }

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var holder = Bean is not null ? BeanHolder.ForInstance(Bean)
            : BeanType is not null ? BeanHolder.ForType(BeanType, Scope)
            : Ref is not null ? BeanHolder.ForName(Ref, Scope)
            : throw new InvalidOperationException("bean requires a bean, a bean type or a registry name");
        if (holder.Type is not null || Scope == BeanScope.Singleton)
        {
            BeanProcessor.Validate(routeContext.Context, holder, Method);
        }

        return new BeanProcessor(holder, Method);
    }
}
