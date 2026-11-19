using Caravan.Spi;

namespace Caravan.Model;

/// <summary>Runs custom code (<c>process</c>).</summary>
public sealed class ProcessDefinition(IProcessor processor) : ProcessorDefinition
{
    /// <summary>The processor to run.</summary>
    public IProcessor Processor { get; set; } = processor;

    /// <summary>Optional registry name when the processor is resolved by reference.</summary>
    public string? Ref { get; set; }

    /// <inheritdoc />
    public override string ShortName => "process";

    /// <inheritdoc />
    public override string Label => Ref is null ? "process" : $"process[ref:{Ref}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => Processor;
}
