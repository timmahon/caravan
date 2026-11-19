namespace Caravan.Support;

/// <summary>Extra hooks for endpoints.</summary>
public static class EndpointBaseExtensions
{
    internal static void OnLenientParameters(this EndpointBase endpoint, IDictionary<string, string> parameters)
    {
        if (endpoint is ILenientEndpoint lenient)
        {
            lenient.SetLenientParameters(parameters);
        }
    }
}
