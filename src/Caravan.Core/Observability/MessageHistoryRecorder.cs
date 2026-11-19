namespace Caravan.Observability;

/// <summary>Adds steps to an exchange's message history.</summary>
internal static class MessageHistoryRecorder
{
    public static MessageHistoryEntry Add(Exchange exchange, string routeId, string nodeId, string label, DateTimeOffset now)
    {
        var entry = new MessageHistoryEntry(routeId, nodeId, label, now);
        if (exchange.GetProperty(ExchangePropertyKeys.MessageHistory) is not List<MessageHistoryEntry> list)
        {
            list = [];
            exchange.SetProperty(ExchangePropertyKeys.MessageHistory, list);
        }

        lock (list)
        {
            list.Add(entry);
        }

        return entry;
    }

    /// <summary>Gives a copied exchange its own history list.</summary>
    public static void CopyInto(Exchange copy)
    {
        if (copy.GetProperty(ExchangePropertyKeys.MessageHistory) is List<MessageHistoryEntry> list)
        {
            lock (list)
            {
                copy.SetProperty(ExchangePropertyKeys.MessageHistory, new List<MessageHistoryEntry>(list));
            }
        }
    }
}
