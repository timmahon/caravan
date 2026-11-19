using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.Log;

/// <summary>
/// The <c>log</c> component: logs exchanges with <see cref="ILogger"/>.
/// <para>URI: <c>log:category[?level=Info&amp;showHeaders=true&amp;...]</c></para>
/// </summary>
public sealed class LogComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) =>
        new LogEndpoint(uri, this, string.IsNullOrEmpty(remaining) ? "caravan" : remaining);
}
