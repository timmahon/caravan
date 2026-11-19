namespace Caravan.Spi;

/// <summary>Sends exchanges to an endpoint.</summary>
public interface IProducer : IProcessor, IService
{
    /// <summary>The endpoint sent to.</summary>
    IEndpoint Endpoint { get; }
}
