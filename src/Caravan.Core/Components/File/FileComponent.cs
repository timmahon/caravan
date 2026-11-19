using Caravan.Support;

namespace Caravan.Components.File;

/// <summary>
/// The <c>file</c> component (Camel's <c>file</c>): consumes files from a directory and writes message bodies to files. See
/// <see cref="GenericFileEndpoint{T}"/> for the options and <c>docs/ARCHITECTURE.md</c> ("File component").
/// <para>URI: <c>file:directory[?options]</c> (<c>file:inbox</c>, <c>file:/var/data/in</c>, <c>file:///var/data/in</c>).</para>
/// </summary>
public sealed class FileComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(remaining))
        {
            throw new ArgumentException("The file endpoint requires a directory, e.g. file:inbox");
        }

        return new FileEndpoint(uri, this, Uri.UnescapeDataString(remaining));
    }

    /// <inheritdoc />
    protected override void AfterConfigure(EndpointBase endpoint)
    {
        try
        {
            ((FileEndpoint)endpoint).Validate();
        }
        catch (ArgumentException e)
        {
            throw new ResolveEndpointFailedException(endpoint.EndpointUri, e.Message, e);
        }
    }
}
