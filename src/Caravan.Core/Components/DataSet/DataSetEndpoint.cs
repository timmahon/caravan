using Caravan.Components.Mock;
using Caravan.Spi;
using Microsoft.Extensions.Logging;

namespace Caravan.Components.DataSet;

/// <summary>
/// A <c>dataset</c> endpoint (Camel's <c>DataSetEndpoint</c>), a <see cref="MockEndpoint"/> expecting <see cref="IDataSet.Size"/> messages
/// (set when it starts).
/// <list type="bullet">
/// <item>Its consumer waits <see cref="InitialDelay"/> (and until the data set holds <see cref="MinRate"/> messages), then sends the messages one
/// at a time, each with the <see cref="CaravanHeaders.DataSetIndex"/> header, <see cref="ProduceDelay"/> apart.</item>
/// <item>Its producer waits <see cref="ConsumeDelay"/> per message, checks the index header (<see cref="DataSetIndex"/>) against the number of
/// messages received so far and the message against the data set (<see cref="IDataSet.AssertMessageExpected"/>); a mismatch fails the
/// exchange and <see cref="MockEndpoint.AssertIsSatisfiedAsync(TimeSpan?)"/>.</item>
/// </list>
/// Delays use the context's clock. Progress is logged every <see cref="IDataSet.ReportCount"/> messages.
/// </summary>
public sealed class DataSetEndpoint : MockEndpoint
{
    private readonly ILogger _logger;

    internal DataSetEndpoint(string uri, DataSetComponent component, string name, IDataSet dataSet)
        : base(uri, component, name)
    {
        DataSet = dataSet;
        _logger = component.Context!.LoggerFactory.CreateLogger<DataSetEndpoint>();
    }

    /// <summary>The data set.</summary>
    public IDataSet DataSet { get; }

    /// <summary>The delay before the consumer sends the first message (default 1s, as Camel).</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The delay between messages sent by the consumer (default 3 ms, as Camel); zero for none.</summary>
    public TimeSpan ProduceDelay { get; set; } = TimeSpan.FromMilliseconds(3);

    /// <summary>The delay the producer waits for each message it receives (default none).</summary>
    public TimeSpan ConsumeDelay { get; set; }

    /// <summary>The consumer waits until the data set holds at least this many messages (default 0).</summary>
    public long MinRate { get; set; }

    /// <summary>
    /// How the producer checks the <see cref="CaravanHeaders.DataSetIndex"/> header: <c>lenient</c> (default; checked when present),
    /// <c>strict</c> (required) or <c>off</c>.
    /// </summary>
    public string DataSetIndex { get; set; } = "lenient";

    /// <inheritdoc />
    public override bool IsLenientProperties => false;

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) => new DataSetConsumer(this, processor);

    /// <summary>Creates an exchange filled with the message <paramref name="messageIndex"/> and its index header.</summary>
    /// <param name="messageIndex">The message number.</param>
    /// <returns>The exchange.</returns>
    public Exchange CreateDataSetExchange(long messageIndex)
    {
        var exchange = CreateExchange();
        DataSet.PopulateMessage(exchange, messageIndex);
        exchange.Message.SetHeader(CaravanHeaders.DataSetIndex, messageIndex);
        return exchange;
    }

    /// <summary>Expects <see cref="IDataSet.Size"/> messages.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        if (DataSetIndex is not ("lenient" or "strict" or "off"))
        {
            throw new ArgumentException($"The dataSetIndex option must be strict, lenient or off, was: {DataSetIndex}");
        }

        ExpectedMessageCount((int)DataSet.Size);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async ValueTask PerformAssertionsAsync(Exchange exchange, int index)
    {
        if (ConsumeDelay > TimeSpan.Zero)
        {
            await Task.Delay(ConsumeDelay, Context.TimeProvider).ConfigureAwait(false);
        }

        var header = exchange.Message.GetHeader(CaravanHeaders.DataSetIndex);
        if (header is null && DataSetIndex == "strict")
        {
            throw new MockAssertionException($"Header: {CaravanHeaders.DataSetIndex} is missing on message: {index}");
        }

        if (header is not null && DataSetIndex != "off")
        {
            var actual = exchange.Message.GetHeader<long>(CaravanHeaders.DataSetIndex);
            if (actual != index)
            {
                throw new MockAssertionException($"Header: {CaravanHeaders.DataSetIndex} does not match. Expected: <{index}> but was: <{actual}>");
            }
        }

        DataSet.AssertMessageExpected(this, CreateDataSetExchange(index), exchange, index);
        ReportProgress(index + 1, "Received");
    }

    internal void ReportProgress(long count, string verb)
    {
        var every = DataSet.ReportCount;
        if (every > 0 && count % every == 0)
        {
            _logger.LogInformation("{Verb} {Count} of {Size} messages on {Uri}", verb, count, DataSet.Size, EndpointUri);
        }
    }
}
