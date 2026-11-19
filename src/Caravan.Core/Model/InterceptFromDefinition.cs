namespace Caravan.Model;

/// <summary>
/// The <c>interceptFrom</c> EIP: <see cref="OutputDefinition.Outputs"/> run when an exchange enters a route whose <c>from</c> endpoint
/// matches <see cref="Uri"/> (all routes of the route builder when null), before the route's first node. The header and property
/// <see cref="CaravanHeaders.InterceptedEndpoint"/> hold the route's endpoint URI. A <c>stop</c> in the block stops the exchange.
/// </summary>
public sealed class InterceptFromDefinition : InterceptDefinition
{
    /// <summary>
    /// The endpoint URI pattern (exact, wildcard such as <c>seda:*</c>, or regular expression; see <see cref="Support.EndpointHelper.MatchEndpoint"/>);
    /// null matches every route.
    /// </summary>
    public string? Uri { get; set; }

    /// <inheritdoc />
    public override string ShortName => "interceptFrom";

    /// <inheritdoc />
    public override string Label => Uri is null ? "interceptFrom" : $"interceptFrom[{Uri}]";
}
