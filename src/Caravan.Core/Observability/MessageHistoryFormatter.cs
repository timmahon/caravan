using System.Globalization;
using System.Text;
using Caravan.Support;

namespace Caravan.Observability;

/// <summary>Renders an exchange's message history as Camel's table (used by error handler logs and the Simple <c>${messageHistory}</c>).</summary>
public static class MessageHistoryFormatter
{
    private static readonly ExchangeFormatter DefaultFormatter = new()
    {
        ShowExchangeId = true,
        ShowHeaders = true,
        ShowBodyType = true,
        ShowBody = true,
        MaxChars = 1000,
    };

    /// <summary>The message history of an exchange (a snapshot), empty when it has none.</summary>
    /// <param name="exchange">The exchange.</param>
    /// <returns>The steps, in order.</returns>
    public static IReadOnlyList<MessageHistoryEntry> GetHistory(Exchange exchange)
    {
        if (exchange.GetProperty(ExchangePropertyKeys.MessageHistory) is not List<MessageHistoryEntry> list)
        {
            return [];
        }

        lock (list)
        {
            return [.. list];
        }
    }

    /// <summary>
    /// Formats the message history of <paramref name="exchange"/>: a table of route id, node id, processor and elapsed milliseconds, then
    /// (optionally) the exchange.
    /// </summary>
    /// <param name="exchange">The exchange.</param>
    /// <param name="includeExchange">Whether the exchange (id, headers, body) is appended.</param>
    /// <param name="formatter">Formats the exchange; null uses a default showing id, headers and body.</param>
    /// <returns>The text; when the exchange has no history, a line saying message history is disabled.</returns>
    public static string Format(Exchange exchange, bool includeExchange = true, ExchangeFormatter? formatter = null)
    {
        var history = GetHistory(exchange);
        var now = exchange.Context.TimeProvider.GetUtcNow();
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine(history.Count == 0 ? "Message History (message history is disabled)" : "Message History");
        var rule = new string('-', 120);
        builder.AppendLine(rule);
        builder.AppendLine(Row("Route ID", "Node ID", "Processor", "Elapsed (ms)"));
        foreach (var entry in history)
        {
            var elapsed = entry.Elapsed ?? (now - entry.Timestamp);
            builder.AppendLine(Row(
                $"[{entry.RouteId}]",
                $"[{entry.NodeId}]",
                $"[{entry.Label}]",
                ((long)elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)));
        }

        if (includeExchange)
        {
            builder.AppendLine();
            builder.AppendLine("Exchange");
            builder.AppendLine(rule);
            builder.AppendLine((formatter ?? DefaultFormatter).Format(exchange));
        }

        builder.Append(rule);
        return builder.ToString();
    }

    private static string Row(string route, string node, string processor, string elapsed) =>
        $"{Fit(route, 30)} {Fit(node, 30)} {Fit(processor, 45)} {elapsed,12}";

    private static string Fit(string text, int width) =>
        text.Length > width ? text[..(width - 3)] + "..." : text.PadRight(width);
}
