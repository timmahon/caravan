using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.Log;

/// <summary>
/// The <c>log</c> component: logs exchanges with <see cref="ILogger"/>.
/// <para>URI: <c>log:category[?level=Info&amp;showHeaders=true&amp;...]</c></para>
/// </summary>
public sealed class LogComponent : ComponentBase
{
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) =>
        new LogEndpoint(uri, this, string.IsNullOrEmpty(remaining) ? "caravan" : remaining);
}

/// <summary>A <c>log</c> endpoint. Options mirror Camel's log component.</summary>
public sealed class LogEndpoint(string uri, LogComponent component, string category)
    : EndpointBase(uri, component, component.Context!)
{
    public string Category { get; } = category;

    public LogLevel Level { get; set; } = LogLevel.Information;

    /// <summary>Optional custom message; when set it replaces the formatted exchange.</summary>
    public string? Message { get; set; }

    /// <summary>Log one line per <c>GroupSize</c> messages with throughput instead of each message.</summary>
    public int? GroupSize { get; set; }

    public ExchangeFormatter Formatter { get; } = new();

    public bool ShowAll { get => Formatter.ShowAll; set => Formatter.ShowAll = value; }

    public bool ShowBody { get => Formatter.ShowBody; set => Formatter.ShowBody = value; }

    public bool ShowBodyType { get => Formatter.ShowBodyType; set => Formatter.ShowBodyType = value; }

    public bool ShowHeaders { get => Formatter.ShowHeaders; set => Formatter.ShowHeaders = value; }

    public bool ShowProperties { get => Formatter.ShowProperties; set => Formatter.ShowProperties = value; }

    public bool ShowExchangeId { get => Formatter.ShowExchangeId; set => Formatter.ShowExchangeId = value; }

    public bool ShowExchangePattern { get => Formatter.ShowExchangePattern; set => Formatter.ShowExchangePattern = value; }

    public bool ShowException { get => Formatter.ShowException; set => Formatter.ShowException = value; }

    public bool Multiline { get => Formatter.Multiline; set => Formatter.Multiline = value; }

    public int MaxChars { get => Formatter.MaxChars; set => Formatter.MaxChars = value; }

    public override IProducer CreateProducer() => new LogProducer(this);

    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("The log component does not support consumers");
}

internal sealed class LogProducer(LogEndpoint endpoint) : ProducerBase(endpoint)
{
    private readonly ILogger _logger = endpoint.Context.LoggerFactory.CreateLogger(endpoint.Category);
    private long _count;
    private long _groupStart;

    public override ValueTask ProcessAsync(Exchange exchange)
    {
        if (!_logger.IsEnabled(endpoint.Level))
        {
            return ValueTask.CompletedTask;
        }

        if (endpoint.GroupSize is > 0 and var size)
        {
            var count = Interlocked.Increment(ref _count);
            if (count == 1)
            {
                _groupStart = endpoint.Context.TimeProvider.GetTimestamp();
            }

            if (count % size == 0)
            {
                var elapsed = endpoint.Context.TimeProvider.GetElapsedTime(_groupStart);
                _logger.Log(endpoint.Level, "Received: {Count} new messages, with total {Total} so far. Last group took: {Elapsed} ms", size, count, (long)elapsed.TotalMilliseconds);
                _groupStart = endpoint.Context.TimeProvider.GetTimestamp();
            }

            return ValueTask.CompletedTask;
        }

        var text = endpoint.Message ?? endpoint.Formatter.Format(exchange);
        if (endpoint.ShowException && exchange.Exception is { } e)
        {
            _logger.Log(endpoint.Level, e, "{Message}", text);
        }
        else
        {
            _logger.Log(endpoint.Level, "{Message}", text);
        }

        return ValueTask.CompletedTask;
    }
}
