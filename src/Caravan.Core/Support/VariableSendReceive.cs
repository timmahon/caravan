namespace Caravan.Support;

/// <summary>
/// Camel 4's <c>variableSend</c>/<c>variableReceive</c> on sending EIPs: the body sent is taken from a variable, and/or the reply is stored in a
/// variable (body as <c>name</c>, each header as <c>name.header.key</c>) while the exchange's message is left as it was.
/// </summary>
internal sealed class VariableSendReceive
{
    private readonly object? _body;
    private readonly Dictionary<string, object?> _headers;

    private VariableSendReceive(Message message)
    {
        _body = message.Body;
        _headers = new Dictionary<string, object?>(message.Headers, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Prepares a send: puts the <paramref name="variableSend"/> variable in the body; null when neither option is set.</summary>
    public static VariableSendReceive? Before(Exchange exchange, string? variableSend, string? variableReceive)
    {
        if (variableSend is null && variableReceive is null)
        {
            return null;
        }

        var state = new VariableSendReceive(exchange.Message);
        if (variableSend is not null)
        {
            exchange.Message.Body = exchange.GetVariable(variableSend);
        }

        return state;
    }

    /// <summary>
    /// Completes a send: with <paramref name="variableReceive"/> the reply goes into the variable (unless the exchange failed) and the message
    /// is restored; with only a send variable the original body is restored unless a reply was expected (<paramref name="inOut"/>).
    /// </summary>
    public void After(Exchange exchange, string? variableReceive, bool inOut)
    {
        if (variableReceive is not null)
        {
            if (!exchange.IsFailed)
            {
                Store(exchange, variableReceive, exchange.Message);
            }

            Restore(exchange.Message);
        }
        else if (!inOut)
        {
            exchange.Message.Body = _body;
        }
    }

    /// <summary>Restores the body and headers the message had before the send.</summary>
    public void Restore(Message message)
    {
        message.Body = _body;
        message.SetHeaders(_headers);
    }

    /// <summary>Stores a message in variables: the body as <paramref name="name"/>, the headers as <c>name.header.key</c> (old ones removed).</summary>
    public static void Store(Exchange exchange, string name, Message? message)
    {
        exchange.RemoveVariable(System.Text.RegularExpressions.Regex.Escape(name + ".header.") + ".*");
        exchange.SetVariable(name, message?.Body);
        if (message is null)
        {
            return;
        }

        foreach (var (key, value) in message.Headers)
        {
            exchange.SetVariable(name + ".header." + key, value);
        }
    }
}
