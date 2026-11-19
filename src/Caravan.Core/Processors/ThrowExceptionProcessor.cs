using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>The <c>throwException</c> EIP: throws the exception created for the exchange.</summary>
public sealed class ThrowExceptionProcessor(Func<Exchange, Exception> factory) : IProcessor
{
    /// <inheritdoc />
    /// <exception cref="Exception">Always: the exception created by the factory.</exception>
    public ValueTask ProcessAsync(Exchange exchange) =>
        throw (factory(exchange) ?? throw new InvalidOperationException("throwException: the factory returned no exception"));

    /// <inheritdoc />
    public override string ToString() => "ThrowException";
}
