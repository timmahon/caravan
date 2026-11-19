namespace Caravan.Model;

/// <summary>A definition that owns a block of nested steps.</summary>
public abstract class OutputDefinition : ProcessorDefinition
{
    /// <summary>The nested steps, in order.</summary>
    public List<ProcessorDefinition> Outputs { get; } = [];

    /// <inheritdoc />
    public override IEnumerable<ProcessorDefinition> Children => Outputs;
}
