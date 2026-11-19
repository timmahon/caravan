using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Scheduler;

internal sealed class SchedulerConsumer(SchedulerEndpoint endpoint, IProcessor processor) : ScheduledPollConsumer(endpoint, processor)
{
    protected override async Task<int> PollAsync(CancellationToken cancellationToken)
    {
        var exchange = CreateExchange();
        if (endpoint.IncludeMetadata)
        {
            var now = Endpoint.Context.TimeProvider.GetUtcNow();
            exchange.SetProperty(ExchangePropertyKeys.TimerName, endpoint.Name);
            exchange.SetProperty(ExchangePropertyKeys.TimerFiredTime, now);
            exchange.Message.SetHeader(CaravanHeaders.FiredTime, now);
            exchange.Message.SetHeader(CaravanHeaders.MessageTimestamp, now.ToUnixTimeMilliseconds());
        }

        await Processor.ProcessAsync(exchange).ConfigureAwait(false);
        LogFailure(exchange);
        return exchange.GetProperty(ExchangePropertyKeys.SchedulerPolledMessages, true) ? 1 : 0;
    }
}
