using System.Diagnostics;
using System.Text;

namespace Caravan.Observability;

/// <summary>Injects and extracts the W3C trace context (<c>traceparent</c>, <c>tracestate</c>, <c>baggage</c>) in message headers.</summary>
internal static class TraceContextPropagator
{
    /// <summary>Writes the context of <paramref name="activity"/> into the headers of the exchange's message.</summary>
    public static void Inject(Exchange exchange, Activity activity, bool baggage)
    {
        if (activity.IdFormat != ActivityIdFormat.W3C || activity.Id is not { } id)
        {
            return;
        }

        var message = exchange.Message;
        message.SetHeader(CaravanSemanticConventions.TraceParentHeader, id);
        if (activity.TraceStateString is { Length: > 0 } state)
        {
            message.SetHeader(CaravanSemanticConventions.TraceStateHeader, state);
        }
        else
        {
            message.RemoveHeader(CaravanSemanticConventions.TraceStateHeader);
        }

        if (baggage)
        {
            StringBuilder? builder = null;
            foreach (var (key, value) in activity.Baggage)
            {
                builder = builder is null ? new StringBuilder() : builder.Append(',');
                builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value ?? string.Empty));
            }

            if (builder is not null)
            {
                message.SetHeader(CaravanSemanticConventions.BaggageHeader, builder.ToString());
            }
        }
    }

    /// <summary>Reads a parent context from the headers of the exchange's message.</summary>
    public static bool TryExtract(Exchange exchange, out ActivityContext context)
    {
        var message = exchange.Message;
        if (message.GetHeader(CaravanSemanticConventions.TraceParentHeader) is string traceParent &&
            ActivityContext.TryParse(traceParent, message.GetHeader(CaravanSemanticConventions.TraceStateHeader) as string, isRemote: true, out context))
        {
            return true;
        }

        context = default;
        return false;
    }

    /// <summary>Adds the baggage of the message's <c>baggage</c> header to <paramref name="activity"/>.</summary>
    public static void ExtractBaggage(Exchange exchange, Activity activity)
    {
        if (exchange.Message.GetHeader(CaravanSemanticConventions.BaggageHeader) is not string { Length: > 0 } header)
        {
            return;
        }

        foreach (var item in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = item.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var value = item[(eq + 1)..];
            var semicolon = value.IndexOf(';', StringComparison.Ordinal);
            if (semicolon >= 0)
            {
                value = value[..semicolon];
            }

            var key = Uri.UnescapeDataString(item[..eq].Trim());
            if (activity.GetBaggageItem(key) is null)
            {
                activity.AddBaggage(key, Uri.UnescapeDataString(value.Trim()));
            }
        }
    }
}
