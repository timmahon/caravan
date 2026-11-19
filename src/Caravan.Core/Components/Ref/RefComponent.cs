using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Ref;

/// <summary>
/// The <c>ref</c> component (Camel's <c>ref</c>): <c>ref:name</c> is the endpoint bound in the registry under <c>name</c> (an
/// <see cref="IEndpoint"/>, or a string URI resolved through the context). An <see cref="EndpointBase"/> bean is returned as is; another
/// <see cref="IEndpoint"/> is wrapped in a <see cref="RefEndpoint"/> that delegates to it.
/// </summary>
public sealed class RefComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        var context = RequiredContext;
        var name = context.ResolvePropertyPlaceholders(remaining);
        var target = context.Registry.Lookup(name) switch
        {
            null => throw new NoSuchBeanException(name),
            IEndpoint endpoint => endpoint,
            string targetUri => context.GetEndpoint(targetUri),
            var other => throw new ArgumentException($"Bean '{name}' is a {other.GetType().Name}, not an endpoint"),
        };
        return target as EndpointBase ?? new RefEndpoint(uri, this, target);
    }
}
