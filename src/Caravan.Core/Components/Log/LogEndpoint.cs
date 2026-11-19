using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.Log;

/// <summary>A <c>log</c> endpoint. Options mirror Camel's log component.</summary>
public sealed class LogEndpoint(string uri, LogComponent component, string category)
    : EndpointBase(uri, component, component.Context!)
{
    /// <summary>The logger category (the URI path).</summary>
    public string Category { get; } = category;

    /// <summary>The level messages are logged at (default <c>Information</c>).</summary>
    public LogLevel Level { get; set; } = LogLevel.Information;

    /// <summary>Optional custom message; when set it replaces the formatted exchange.</summary>
    public string? Message { get; set; }

    /// <summary>Log one line per <c>GroupSize</c> messages with throughput instead of each message.</summary>
    public int? GroupSize { get; set; }

    /// <summary>Formats the exchange; the <c>Show*</c> options below configure it.</summary>
    public ExchangeFormatter Formatter { get; } = new();

    /// <summary>Shows everything (overrides the individual options).</summary>
    public bool ShowAll { get => Formatter.ShowAll; set => Formatter.ShowAll = value; }

    /// <summary>Shows the body (default true).</summary>
    public bool ShowBody { get => Formatter.ShowBody; set => Formatter.ShowBody = value; }

    /// <summary>Shows the body type (default true).</summary>
    public bool ShowBodyType { get => Formatter.ShowBodyType; set => Formatter.ShowBodyType = value; }

    /// <summary>Shows the message headers.</summary>
    public bool ShowHeaders { get => Formatter.ShowHeaders; set => Formatter.ShowHeaders = value; }

    /// <summary>Shows the exchange properties.</summary>
    public bool ShowProperties { get => Formatter.ShowProperties; set => Formatter.ShowProperties = value; }

    /// <summary>Shows the exchange id.</summary>
    public bool ShowExchangeId { get => Formatter.ShowExchangeId; set => Formatter.ShowExchangeId = value; }

    /// <summary>Shows the exchange pattern (default true).</summary>
    public bool ShowExchangePattern { get => Formatter.ShowExchangePattern; set => Formatter.ShowExchangePattern = value; }

    /// <summary>Shows the exception, if any.</summary>
    public bool ShowException { get => Formatter.ShowException; set => Formatter.ShowException = value; }

    /// <summary>Writes each part on its own line.</summary>
    public bool Multiline { get => Formatter.Multiline; set => Formatter.Multiline = value; }

    /// <summary>Clips the logged body after this many characters (default 10000; 0 or less = no limit).</summary>
    public int MaxChars { get => Formatter.MaxChars; set => Formatter.MaxChars = value; }

    /// <inheritdoc />
    public override IProducer CreateProducer() => new LogProducer(this);

    /// <summary>Not supported: the log component only has producers.</summary>
    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("The log component does not support consumers");
}
