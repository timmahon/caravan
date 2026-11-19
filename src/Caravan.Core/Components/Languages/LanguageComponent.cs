using Caravan.Support;

namespace Caravan.Components.Languages;

/// <summary>
/// The <c>language</c> component (Camel's <c>language</c>): <c>language:languageName[:script]?transform=true</c> evaluates the script (or, without
/// one, the message body as the script) in a language of the context and makes the result the body. Producer only. Scripts with <c>?</c> or
/// <c>&amp;</c> go in <c>RAW(...)</c>.
/// </summary>
public sealed class LanguageComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        var colon = remaining.IndexOf(':', StringComparison.Ordinal);
        var language = colon < 0 ? remaining : remaining[..colon];
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("language endpoint requires a language name, e.g. language:simple:Hello ${body}");
        }

        return new LanguageEndpoint(uri, this, language, colon < 0 ? null : remaining[(colon + 1)..]);
    }
}
