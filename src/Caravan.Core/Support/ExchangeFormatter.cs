using System.Collections;
using System.Text;

namespace Caravan.Support;

/// <summary>Formats an exchange for logging, like Camel's <c>DefaultExchangeFormatter</c>.</summary>
public sealed class ExchangeFormatter
{
    /// <summary>Shows everything (overrides the individual options).</summary>
    public bool ShowAll { get; set; }

    /// <summary>Shows the body (default true).</summary>
    public bool ShowBody { get; set; } = true;

    /// <summary>Shows the body type (default true).</summary>
    public bool ShowBodyType { get; set; } = true;

    /// <summary>Shows the message headers.</summary>
    public bool ShowHeaders { get; set; }

    /// <summary>Shows the exchange properties.</summary>
    public bool ShowProperties { get; set; }

    /// <summary>Shows the exchange id.</summary>
    public bool ShowExchangeId { get; set; }

    /// <summary>Shows the exchange pattern (default true).</summary>
    public bool ShowExchangePattern { get; set; } = true;

    /// <summary>Shows the exception, if any.</summary>
    public bool ShowException { get; set; }

    /// <summary>Writes each part on its own line.</summary>
    public bool Multiline { get; set; }

    /// <summary>Maximum characters of the body to include (default 10000; 0 or less means unlimited).</summary>
    public int MaxChars { get; set; } = 10000;

    /// <summary>Formats the exchange according to the options.</summary>
    public string Format(Exchange exchange)
    {
        var parts = new List<string>();
        if (ShowAll || ShowExchangeId)
        {
            parts.Add($"Id: {exchange.ExchangeId}");
        }

        if (ShowAll || ShowExchangePattern)
        {
            parts.Add($"ExchangePattern: {exchange.Pattern}");
        }

        if (ShowAll || ShowProperties)
        {
            parts.Add($"Properties: {FormatMap(exchange.Properties)}");
        }

        if (ShowAll || ShowHeaders)
        {
            parts.Add($"Headers: {FormatMap(exchange.Message.Headers)}");
        }

        if (ShowAll || ShowBodyType)
        {
            parts.Add($"BodyType: {exchange.Message.Body?.GetType().Name ?? "null"}");
        }

        if (ShowAll || ShowBody)
        {
            parts.Add($"Body: {FormatBody(exchange.Message.Body)}");
        }

        if ((ShowAll || ShowException) && exchange.Exception is { } e)
        {
            parts.Add($"Exception: {e.GetType().Name}: {e.Message}");
        }

        var separator = Multiline ? Environment.NewLine + "    " : ", ";
        var prefix = Multiline ? "Exchange[" + Environment.NewLine + "    " : "Exchange[";
        var suffix = Multiline ? Environment.NewLine + "]" : "]";
        return prefix + string.Join(separator, parts) + suffix;
    }

    private string FormatBody(object? body)
    {
        var text = body switch
        {
            null => "[Body is null]",
            string s => s,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            Stream => "[Body is instance of Stream]",
            IDictionary dict => FormatMap(dict.Cast<DictionaryEntry>().ToDictionary(d => d.Key.ToString()!, d => d.Value)),
            IEnumerable e => "[" + string.Join(", ", e.Cast<object?>()) + "]",
            _ => body.ToString() ?? string.Empty,
        };

        return MaxChars > 0 && text.Length > MaxChars ? text[..MaxChars] + "... [Body clipped after " + MaxChars + " chars, total length is " + text.Length + "]" : text;
    }

    private static string FormatMap(IEnumerable<KeyValuePair<string, object?>> map) =>
        "{" + string.Join(", ", map.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}={kv.Value}")) + "}";
}
