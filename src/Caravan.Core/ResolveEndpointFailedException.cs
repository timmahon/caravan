namespace Caravan;

/// <summary>An endpoint URI could not be resolved.</summary>
public class ResolveEndpointFailedException(string uri, string message, Exception? innerException = null)
    : CaravanException($"Failed to resolve endpoint: {uri} due to: {message}", innerException)
{
    /// <summary>The URI that could not be resolved.</summary>
    public string Uri { get; } = uri;
}
