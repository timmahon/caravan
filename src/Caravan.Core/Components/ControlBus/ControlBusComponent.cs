using Caravan.Support;

namespace Caravan.Components.ControlBus;

/// <summary>
/// The <c>controlbus</c> component (Camel's <c>controlbus</c>, the Control Bus EIP): manages routes and evaluates expressions from messages.
/// <list type="bullet">
/// <item><c>controlbus:route?routeId=x&amp;action=start|stop|suspend|resume|restart|status|stats[&amp;async=true]</c>: <c>status</c> sets the
/// route status (e.g. <c>Started</c>) as the body, <c>stats</c> an XML summary of the route's counters (without <c>routeId</c>: the
/// context's status / statistics); <c>routeId=current</c> is the route of the exchange. With <c>async</c> the action runs in the background
/// (needed to stop the route that sends the message) and nothing is returned.</item>
/// <item><c>controlbus:language:name</c>: the body is an expression in the language <c>name</c>, evaluated on the exchange; a non-null result
/// becomes the body.</item>
/// </list>
/// Producer only.
/// </summary>
public sealed class ControlBusComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        if (remaining == "route")
        {
            return new ControlBusEndpoint(uri, this, "route", null);
        }

        if (remaining.StartsWith("language:", StringComparison.Ordinal) && remaining.Length > "language:".Length)
        {
            return new ControlBusEndpoint(uri, this, "language", remaining["language:".Length..]);
        }

        throw new ArgumentException($"Unknown controlbus command: {remaining} (use controlbus:route or controlbus:language:name)");
    }
}
