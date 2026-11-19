namespace Caravan;

/// <summary>A bean could not be found in the registry.</summary>
public class NoSuchBeanException(string name)
    : CaravanException($"No bean could be found in the registry for: {name}")
{
    /// <summary>The name that was looked up.</summary>
    public string Name { get; } = name;
}
