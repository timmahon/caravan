using Microsoft.AspNetCore.Builder;

namespace Caravan.AspNetCore;

/// <summary>Applies conventions to several endpoint convention builders.</summary>
internal sealed class CompositeEndpointConventionBuilder(IReadOnlyList<IEndpointConventionBuilder> builders) : IEndpointConventionBuilder
{
    public void Add(Action<EndpointBuilder> convention)
    {
        foreach (var builder in builders)
        {
            builder.Add(convention);
        }
    }

    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        foreach (var builder in builders)
        {
            builder.Finally(finallyConvention);
        }
    }
}
