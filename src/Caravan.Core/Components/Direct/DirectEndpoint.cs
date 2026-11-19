using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Direct;

/// <summary>A <c>direct</c> endpoint.</summary>
public sealed class DirectEndpoint(string uri, DirectComponent component, string name)
    : EndpointBase(uri, component, component.Context!)
{
    /// <summary>The endpoint name (the URI path), which identifies the consumer.</summary>
    public string Name { get; } = name;

    /// <summary>Wait for a consumer to become available when sending (default true).</summary>
    public bool Block { get; set; } = true;

    /// <summary>How long to wait for a consumer when <see cref="Block"/> is true (default 30s).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Fail when there is no consumer (default true). When false the message is silently dropped.</summary>
    public bool FailIfNoConsumers { get; set; } = true;

    internal DirectComponent DirectComponent => (DirectComponent)Component!;

    /// <inheritdoc />
    public override IProducer CreateProducer() => new DirectProducer(this);

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) => new DirectConsumer(this, processor);
}
