using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.Log;

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
