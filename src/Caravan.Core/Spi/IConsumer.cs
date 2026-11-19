namespace Caravan.Spi;

/// <summary>Receives messages from an endpoint and feeds them to a processor.</summary>
public interface IConsumer : IService
{
    /// <summary>The endpoint consumed from.</summary>
    IEndpoint Endpoint { get; }

    /// <summary>The processor (typically the route) receiving the exchanges.</summary>
    IProcessor Processor { get; }
}
