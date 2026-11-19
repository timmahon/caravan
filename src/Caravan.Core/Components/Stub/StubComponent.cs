using Caravan.Components.Seda;

namespace Caravan.Components.Stub;

/// <summary>
/// The <c>stub</c> component (Camel's <c>stub</c>): stands in for any endpoint during development or tests, e.g. <c>stub:jms:queue:orders</c>
/// or <c>stub:kafka:topic?brokers=x</c>. It behaves like <c>seda</c> (an in-memory queue named after the whole path, consumers, request/reply)
/// and ignores the URI options it does not know. Stub queues are separate from <c>seda</c> queues.
/// </summary>
public sealed class StubComponent : SedaComponent
{
    /// <inheritdoc />
    protected override string Scheme => "stub";

    /// <inheritdoc />
    internal override bool LenientEndpoints => true;
}
