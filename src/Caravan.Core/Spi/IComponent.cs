namespace Caravan.Spi;

/// <summary>A factory of endpoints for a URI scheme (e.g. <c>direct</c>, <c>seda</c>).</summary>
public interface IComponent : IService
{
    /// <summary>The context the component belongs to; set when the component is added.</summary>
    ICaravanContext? Context { get; set; }

    /// <summary>Creates an endpoint for the full <paramref name="uri"/>.</summary>
    IEndpoint CreateEndpoint(string uri);
}
