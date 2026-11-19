using Caravan.Support;

namespace Caravan.Components.Mock;

/// <summary>
/// The <c>mock</c> component for testing: records received exchanges and verifies expectations.
/// <para>URI: <c>mock:name[?retainFirst=..&amp;retainLast=..]</c></para>
/// </summary>
public sealed class MockComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) =>
        new MockEndpoint(uri, this, remaining);
}
