using System.Text;
using Caravan.Spi;
using Microsoft.Extensions.Configuration;

namespace Caravan.Impl;

/// <summary>The default <see cref="IPropertiesComponent"/> (Camel's <c>PropertiesComponent</c>); see the interface for the syntax.</summary>
public sealed class DefaultPropertiesComponent : IPropertiesComponent
{
    private const string Prefix = "{{";
    private const string Suffix = "}}";

    // Stands for a missing optional placeholder until the whole text is resolved.
    private const string OptionalMarker = "\u0000caravan-optional\u0000";
    private const int MaxDepth = 32;

    /// <summary>Creates a component reading <paramref name="sources"/> (shared with the context).</summary>
    public DefaultPropertiesComponent(IList<IPropertiesSource>? sources = null)
    {
        Sources = sources ?? new List<IPropertiesSource>();
    }

    /// <inheritdoc />
    public IDictionary<string, string?> InitialProperties { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IDictionary<string, string?> OverrideProperties { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IList<IPropertiesSource> Sources { get; }

    /// <inheritdoc />
    public EnvironmentVariableMode EnvironmentVariableMode { get; set; } = EnvironmentVariableMode.Never;

    /// <inheritdoc />
    public void AddConfiguration(IConfiguration configuration) => Sources.Add(new ConfigurationPropertiesSource(configuration));

    /// <inheritdoc />
    public string? ResolveProperty(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var (kind, name) = SplitPrefix(key);
        var value = Lookup(kind, name);
        return value is null ? null : Finish(ResolveValue(name, value, [], key, 0), uri: false);
    }

    /// <inheritdoc />
    public string Resolve(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ResolvePlaceholders(Prefix + key + Suffix);
    }

    /// <inheritdoc />
    public string ResolvePlaceholders(string text) =>
        string.IsNullOrEmpty(text) || !text.Contains(Prefix, StringComparison.Ordinal)
            ? text
            : Finish(Parse(text, [], text, 0), uri: false);

    /// <inheritdoc />
    public string ParseUri(string uri) =>
        string.IsNullOrEmpty(uri) || !uri.Contains(Prefix, StringComparison.Ordinal)
            ? uri
            : Finish(Parse(uri, [], uri, 0), uri: true);

    private static string Finish(string resolved, bool uri)
    {
        if (!resolved.Contains(OptionalMarker, StringComparison.Ordinal))
        {
            return resolved;
        }

        if (!uri)
        {
            return resolved.Replace(OptionalMarker, string.Empty, StringComparison.Ordinal);
        }

        var question = resolved.IndexOf('?', StringComparison.Ordinal);
        if (question < 0)
        {
            return resolved.Replace(OptionalMarker, string.Empty, StringComparison.Ordinal);
        }

        var path = resolved[..question].Replace(OptionalMarker, string.Empty, StringComparison.Ordinal);
        var options = resolved[(question + 1)..]
            .Split('&')
            .Where(option => !option.Contains(OptionalMarker, StringComparison.Ordinal))
            .ToList();
        return options.Count == 0 ? path : path + "?" + string.Join('&', options);
    }

    private string Parse(string text, HashSet<string> visiting, string original, int depth)
    {
        if (!text.Contains(Prefix, StringComparison.Ordinal))
        {
            return text;
        }

        if (depth > MaxDepth)
        {
            throw new ArgumentException($"Too deeply nested property placeholders in: {original}");
        }

        var sb = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var start = text.IndexOf(Prefix, i, StringComparison.Ordinal);
            var end = start < 0 ? -1 : FindEnd(text, start);
            if (end < 0)
            {
                sb.Append(text, i, text.Length - i);
                break;
            }

            sb.Append(text, i, start - i);
            sb.Append(ResolvePlaceholder(text[(start + Prefix.Length)..end], visiting, original, depth));
            i = end + Suffix.Length;
        }

        return sb.ToString();
    }

    private static int FindEnd(string text, int start)
    {
        var depth = 0;
        var i = start;
        while (i < text.Length - 1)
        {
            if (text[i] == '{' && text[i + 1] == '{')
            {
                depth++;
                i += 2;
            }
            else if (text[i] == '}' && text[i + 1] == '}')
            {
                if (--depth == 0)
                {
                    return i;
                }

                i += 2;
            }
            else
            {
                i++;
            }
        }

        return -1;
    }

    private string ResolvePlaceholder(string content, HashSet<string> visiting, string original, int depth)
    {
        var optional = content.StartsWith('?');
        if (optional)
        {
            content = content[1..];
        }

        var (kind, rest) = SplitPrefix(content);
        var colon = IndexOfTopLevelColon(rest);
        var keyPart = colon < 0 ? rest : rest[..colon];
        var defaultPart = colon < 0 ? null : rest[(colon + 1)..];
        var key = Parse(keyPart, visiting, original, depth + 1).Trim();
        var value = Lookup(kind, key);
        if (value is not null)
        {
            return ResolveValue(key, value, visiting, original, depth);
        }

        if (defaultPart is not null)
        {
            return Parse(defaultPart, visiting, original, depth + 1);
        }

        if (optional)
        {
            return OptionalMarker;
        }

        throw new ArgumentException($"Property with key [{(kind is null ? key : kind + ":" + key)}] not found in properties from text: {original}");
    }

    private string ResolveValue(string key, string value, HashSet<string> visiting, string original, int depth)
    {
        if (!value.Contains(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        if (!visiting.Add(key))
        {
            throw new ArgumentException($"Circular reference detected with key [{key}] from text: {original}");
        }

        try
        {
            return Parse(value, visiting, original, depth + 1);
        }
        finally
        {
            visiting.Remove(key);
        }
    }

    private static (string? Kind, string Remainder) SplitPrefix(string content)
    {
        foreach (var kind in (string[])["env", "sys"])
        {
            if (content.Length > kind.Length && content.StartsWith(kind, StringComparison.Ordinal) && content[kind.Length] == ':')
            {
                return (kind, content[(kind.Length + 1)..]);
            }
        }

        return (null, content);
    }

    private static int IndexOfTopLevelColon(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (i < text.Length - 1 && text[i] == '{' && text[i + 1] == '{')
            {
                depth++;
                i++;
            }
            else if (i < text.Length - 1 && text[i] == '}' && text[i + 1] == '}')
            {
                depth--;
                i++;
            }
            else if (text[i] == ':' && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private string? Lookup(string? kind, string key)
    {
        switch (kind)
        {
            case "env":
                return GetEnvironmentVariable(key);
            case "sys":
                return AppContext.GetData(key)?.ToString();
        }

        if (OverrideProperties.TryGetValue(key, out var overridden) && overridden is not null)
        {
            return overridden;
        }

        if (EnvironmentVariableMode == EnvironmentVariableMode.Override && GetEnvironmentVariable(key) is { } env)
        {
            return env;
        }

        foreach (var source in Sources.ToArray())
        {
            if (source.GetProperty(key) is { } value)
            {
                return value;
            }
        }

        if (InitialProperties.TryGetValue(key, out var initial) && initial is not null)
        {
            return initial;
        }

        return EnvironmentVariableMode == EnvironmentVariableMode.Fallback ? GetEnvironmentVariable(key) : null;
    }

    private static string? GetEnvironmentVariable(string key)
    {
        if (key.Length == 0)
        {
            return null;
        }

        return Environment.GetEnvironmentVariable(key)
            ?? Environment.GetEnvironmentVariable(key.ToUpperInvariant().Replace('.', '_').Replace('-', '_'));
    }
}
