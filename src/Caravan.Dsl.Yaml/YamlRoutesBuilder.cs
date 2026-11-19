using Caravan.Builder;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml;

/// <summary>
/// Routes, route templates, templated routes, route configurations and beans written in Apache Camel's YAML DSL:
/// <c>context.AddRoutes(YamlRoutesBuilder.FromFile("routes.yaml"))</c>.
/// </summary>
/// <remarks>
/// The YAML text is parsed when the builder is created (syntax errors are thrown then); it is interpreted each time the builder is built
/// (<see cref="IRoutesBuilder.Build"/>, i.e. when added to a context), since languages, types, data formats and beans are resolved through that
/// context. Beans are bound in the context's registry first. The file's <c>errorHandler</c>, <c>onException</c>, <c>onCompletion</c> and
/// interceptors apply to its routes and templates, like those of a C# <see cref="RouteBuilder"/>. Every error is a
/// <see cref="YamlRouteDefinitionException"/> with the source name, line and column.
/// </remarks>
public sealed class YamlRoutesBuilder : RouteBuilder
{
    private readonly IReadOnlyList<YamlDocument> _documents;

    private YamlRoutesBuilder(string sourceName, IReadOnlyList<YamlDocument> documents)
    {
        SourceName = sourceName;
        _documents = documents;
    }

    /// <summary>The file name, or the name given to the text (used in error messages).</summary>
    public string SourceName { get; }

    /// <summary>Parses a YAML DSL file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="YamlRouteDefinitionException">The file is not valid YAML.</exception>
    public static YamlRoutesBuilder FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new StreamReader(path);
        return Parse(reader, path);
    }

    /// <summary>Parses YAML DSL text.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="sourceName">A name for error messages (default <c>&lt;string&gt;</c>).</param>
    /// <returns>The builder.</returns>
    /// <exception cref="YamlRouteDefinitionException">The text is not valid YAML.</exception>
    public static YamlRoutesBuilder FromString(string yaml, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        return Parse(new StringReader(yaml), sourceName ?? "<string>");
    }

    /// <summary>Parses YAML DSL text read from a stream (UTF-8 unless the stream has a byte order mark); the stream is not disposed.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="sourceName">A name for error messages (default <c>&lt;stream&gt;</c>).</param>
    /// <returns>The builder.</returns>
    /// <exception cref="YamlRouteDefinitionException">The text is not valid YAML.</exception>
    public static YamlRoutesBuilder FromStream(Stream stream, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, leaveOpen: true);
        return Parse(reader, sourceName ?? "<stream>");
    }

    /// <inheritdoc />
    public override void Configure() => new YamlDslLoader(this, new LoadContext(Context, SourceName)).Load(_documents);

    /// <inheritdoc />
    public override string ToString() => $"YamlRoutesBuilder({SourceName})";

    private static YamlRoutesBuilder Parse(TextReader reader, string sourceName)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(reader);
        }
        catch (YamlException e)
        {
            var reason = e.InnerException is YamlException inner && inner.Message != e.Message ? $"{e.Message}: {inner.Message}" : e.Message;
            throw new YamlRouteDefinitionException(
                "Invalid YAML: " + reason,
                sourceName,
                checked((int)e.Start.Line),
                checked((int)e.Start.Column),
                e);
        }

        return new YamlRoutesBuilder(sourceName, [.. stream.Documents]);
    }
}
