using Caravan.Spi;
using Caravan.Support;

namespace Caravan.AspNetCore;

/// <summary>A <c>platform-http</c> consumer: registered with the <see cref="PlatformHttpEngine"/> (an ASP.NET Core endpoint) while started.</summary>
public sealed class PlatformHttpConsumer : ConsumerBase
{
    internal PlatformHttpConsumer(PlatformHttpEndpoint endpoint, IProcessor processor)
        : base(endpoint, processor)
    {
        PlatformHttpEndpoint = endpoint;
    }

    /// <summary>The endpoint.</summary>
    public PlatformHttpEndpoint PlatformHttpEndpoint { get; }

    /// <inheritdoc />
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        PlatformHttpEndpoint.PlatformHttpComponent.Engine.Register(this);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task DoStopAsync(CancellationToken cancellationToken)
    {
        PlatformHttpEndpoint.PlatformHttpComponent.Engine.Unregister(this);
        return Task.CompletedTask;
    }
}
