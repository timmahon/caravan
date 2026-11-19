using YamlDotNet.RepresentationModel;

namespace Caravan.Dsl.Yaml;

/// <summary>What the loader needs while it turns YAML nodes into definitions: the context (languages, types, data formats) and the source name.</summary>
internal sealed class LoadContext(ICaravanContext context, string sourceName)
{
    /// <summary>The context the routes are added to.</summary>
    public ICaravanContext Context { get; } = context;

    /// <summary>The file name (or the name given to the text).</summary>
    public string SourceName { get; } = sourceName;

    /// <summary>Whether a route template is being read (its <c>{{parameters}}</c> are substituted later, per route).</summary>
    public bool InTemplate { get; set; }

    /// <summary>
    /// Whether a typed option of the template being read uses a <c>{{parameter}}</c>: its steps are then read again for each route created from
    /// the template, with the parameters substituted.
    /// </summary>
    public bool TemplateNeedsRebuild { get; set; }

    /// <summary>Rewrites every scalar read (route template parameters when a template's steps are rebuilt per route), or null.</summary>
    public Func<string, string>? Substitute { get; init; }

    /// <summary>The text of a scalar after <see cref="Substitute"/>.</summary>
    public string Text(string text) => Substitute?.Invoke(text) ?? text;

    /// <summary>An error located at <paramref name="node"/>.</summary>
    public YamlRouteDefinitionException Error(YamlNode node, string reason, Exception? innerException = null) =>
        new(reason, SourceName, checked((int)node.Start.Line), checked((int)node.Start.Column), innerException);
}
