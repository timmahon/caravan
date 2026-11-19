using Caravan.Support;

namespace Caravan.Components.Browse;

/// <summary>
/// The <c>browse</c> component (Camel's <c>browse</c>): <c>browse:name</c> keeps the exchanges sent to it so they can be inspected
/// (<see cref="BrowseEndpoint.Exchanges"/>), and passes them on to the route consuming from it, if any (synchronously, like <c>direct</c>).
/// </summary>
public sealed class BrowseComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(remaining))
        {
            throw new ArgumentException("The browse endpoint requires a name, e.g. browse:audit");
        }

        return new BrowseEndpoint(uri, this, remaining);
    }
}
