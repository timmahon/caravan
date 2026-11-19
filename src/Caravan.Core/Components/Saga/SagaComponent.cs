using Caravan.Support;

namespace Caravan.Components.Saga;

/// <summary>
/// The <c>saga</c> component: ends the saga the exchange belongs to (the <see cref="CaravanHeaders.SagaLongRunningAction"/> header), typically one
/// started in <see cref="Model.SagaCompletionMode.Manual"/> mode.
/// <para>URIs: <c>saga:complete</c>, <c>saga:compensate</c></para>
/// </summary>
public sealed class SagaComponent : ComponentBase
{
    /// <inheritdoc />
    /// <exception cref="ArgumentException">The action is neither <c>complete</c> nor <c>compensate</c>.</exception>
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        var action = remaining.ToLowerInvariant() switch
        {
            "complete" => SagaEndpointAction.Complete,
            "compensate" => SagaEndpointAction.Compensate,
            _ => throw new ArgumentException($"saga endpoint action must be 'complete' or 'compensate', not '{remaining}': {uri}"),
        };
        return new SagaEndpoint(uri, this, action);
    }
}
