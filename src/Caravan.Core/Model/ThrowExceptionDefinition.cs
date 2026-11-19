using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>throwException</c> EIP: fails the exchange with an exception created by <see cref="Factory"/>.</summary>
public sealed class ThrowExceptionDefinition(Func<Exchange, Exception> factory) : ProcessorDefinition
{
    /// <summary>Creates the exception to throw for an exchange.</summary>
    public Func<Exchange, Exception> Factory { get; set; } = factory;

    /// <summary>The exception type, when known (for display).</summary>
    public Type? ExceptionType { get; set; }

    /// <inheritdoc />
    public override string ShortName => "throwException";

    /// <inheritdoc />
    public override string Label => ExceptionType is null ? "throwException" : $"throwException[{ExceptionType.Name}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new ThrowExceptionProcessor(Factory);
}
