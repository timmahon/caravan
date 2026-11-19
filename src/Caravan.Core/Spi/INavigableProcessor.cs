namespace Caravan.Spi;

/// <summary>A processor that wraps other processors (pipeline, choice, filter, ...).</summary>
public interface INavigableProcessor : IProcessor
{
    /// <summary>The wrapped processors.</summary>
    IReadOnlyList<IProcessor> Next { get; }
}
