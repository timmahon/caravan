using System.Collections;
using System.Text;

namespace Caravan.Support;

/// <summary>Formats an exchange for logging, like Camel's <c>DefaultExchangeFormatter</c>.</summary>
public sealed class ExchangeFormatter
{
    public bool ShowAll { get; set; }

    public bool ShowBody { get; set; } = true;

    public bool ShowBodyType { get; set; } = true;

    public bool ShowHeaders { get; set; }

    public bool ShowProperties { get; set; }

    public bool ShowExchangeId { get; set; }

    public bool ShowExchangePattern { get; set; } = true;

    public bool ShowException { get; set; }

    public bool Multiline { get; set; }

    /// <summary>Maximum characters of the body to include (default 10000; 0 or less means unlimited).</summary>
    public int MaxChars { get; set; } = 10000;

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
