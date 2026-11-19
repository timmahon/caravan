namespace Caravan.Support;

/// <summary>Implemented by lenient endpoints that want the parameters which did not match a property.</summary>
public interface ILenientEndpoint
{
    /// <summary>Receives the URI parameters that do not match any endpoint property.</summary>
    void SetLenientParameters(IDictionary<string, string> parameters);
}
