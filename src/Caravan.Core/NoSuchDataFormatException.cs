namespace Caravan;

/// <summary>Thrown when no data format is registered under a name.</summary>
public class NoSuchDataFormatException(string name)
    : CaravanException($"No data format could be found for: {name}")
{
    /// <summary>The data format name that was looked up.</summary>
    public string Name { get; } = name;
}
