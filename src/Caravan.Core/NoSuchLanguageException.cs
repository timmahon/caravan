namespace Caravan;

/// <summary>Thrown when no language is registered under a name (Camel's <c>NoSuchLanguageException</c>).</summary>
public class NoSuchLanguageException(string language)
    : CaravanException($"No language could be found for: {language}")
{
    /// <summary>The language name that was looked up.</summary>
    public string Language { get; } = language;
}
