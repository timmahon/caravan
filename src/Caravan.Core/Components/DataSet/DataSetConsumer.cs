using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.DataSet;

/// <summary>Sends the messages of a <c>dataset</c> endpoint's data set, one at a time, on a background task.</summary>
internal sealed class DataSetConsumer(DataSetEndpoint endpoint, IProcessor processor) : ConsumerBase(endpoint, processor)
{
    private readonly ILogger _logger = endpoint.Context.LoggerFactory.CreateLogger<DataSetConsumer>();
    private CancellationTokenSource? _stopping;
    private Task? _loop;

    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(endpoint.Context.ShutdownToken);
        var token = _stopping.Token;
        _loop = Task.Run(() => SendAsync(token), CancellationToken.None);
        return Task.CompletedTask;
    }

    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is { } stopping)
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            if (_loop is { } loop)
            {
                await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            stopping.Dispose();
            _stopping = null;
            _loop = null;
        }
    }

    private async Task SendAsync(CancellationToken token)
    {
        var time = endpoint.Context.TimeProvider;
        try
        {
            if (endpoint.InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(endpoint.InitialDelay, time, token).ConfigureAwait(false);
            }

            while (endpoint.DataSet.Size < endpoint.MinRate)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), time, token).ConfigureAwait(false);
            }

            var size = endpoint.DataSet.Size;
            for (long i = 0; i < size && !token.IsCancellationRequested; i++)
            {
                var exchange = endpoint.CreateDataSetExchange(i);
                exchange.CancellationToken = token;
                await Processor.ProcessAsync(exchange).ConfigureAwait(false);
                if (exchange.Exception is { } failure)
                {
                    _logger.LogWarning(failure, "Message {Index} of data set {Uri} failed", i, endpoint.EndpointUri);
                }

                endpoint.ReportProgress(i + 1, "Sent");
                if (endpoint.ProduceDelay > TimeSpan.Zero && i + 1 < size)
                {
                    await Task.Delay(endpoint.ProduceDelay, time, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // stopped
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error sending the data set of {Uri}", endpoint.EndpointUri);
        }
    }
}
