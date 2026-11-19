using Caravan.Support;

namespace Caravan.Components.Bean;

/// <summary>
/// The <c>class</c> component (Camel's <c>class</c>): <c>class:Full.Type.Name[?method=foo&amp;scope=...&amp;bean.property=value]</c> calls a
/// method of an instance of the type, taken from the context's service provider or created (constructor parameters from the service
/// provider), kept according to the scope (default one per endpoint); for a static class its static methods are called. Producer only.
/// </summary>
public sealed class ClassComponent : BeanComponent
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        var type = Language.Simple.TypeNameResolver.Resolve(remaining) ?? throw new ArgumentException($"Cannot find class: {remaining}");
        var (scope, options) = TakeBeanOptions(parameters);
        return new BeanEndpoint(uri, this, BeanHolder.ForType(type, scope, options), type.FullName ?? type.Name) { Scope = scope };
    }
}
