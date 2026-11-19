namespace Caravan;

/// <summary>No component is registered for the URI scheme.</summary>
public class NoSuchEndpointException(string uri, string message)
    : CaravanException($"No endpoint could be found for: {uri}, please check your classpath contains the needed Caravan component package. {message}")
{
    /// <summary>The URI that could not be resolved.</summary>
    public string Uri { get; } = uri;
}
