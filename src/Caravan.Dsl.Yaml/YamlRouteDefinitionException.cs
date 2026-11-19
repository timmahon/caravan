namespace Caravan.Dsl.Yaml;

/// <summary>
/// A YAML route definition is invalid: a syntax error, an unknown step or option, a value of the wrong type, a missing required option, an unknown
/// language, type or data format... The message starts with the source name, line and column (<c>routes.yaml:12:7: ...</c>).
/// </summary>
public sealed class YamlRouteDefinitionException : CaravanException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="reason">What is wrong.</param>
    /// <param name="sourceName">The file name (or the name given to the YAML text).</param>
    /// <param name="line">The 1-based line, or 0 when unknown.</param>
    /// <param name="column">The 1-based column, or 0 when unknown.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public YamlRouteDefinitionException(string reason, string sourceName, int line, int column, Exception? innerException = null)
        : base($"{sourceName}:{line}:{column}: {reason}", innerException)
    {
        Reason = reason;
        SourceName = sourceName;
        Line = line;
        Column = column;
    }

    /// <summary>What is wrong (the message without the location).</summary>
    public string Reason { get; }

    /// <summary>The file name, or the name given to the YAML text.</summary>
    public string SourceName { get; }

    /// <summary>The 1-based line of the offending node.</summary>
    public int Line { get; }

    /// <summary>The 1-based column of the offending node.</summary>
    public int Column { get; }
}
