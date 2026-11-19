using Caravan.Builder;
using static Caravan.Builder.Lang;

namespace Caravan.Samples.FileBatch;

/// <summary>
/// Polls <c>batch.inbox</c> for CSV files (moved to <c>.done</c> once read), summarizes each file and aggregates all the files of a poll
/// (<c>completionFromBatchConsumer</c>) into one report written to <c>batch.outbox</c>. Files that cannot be read go to <c>.failed</c>.
/// </summary>
public sealed class BatchRoutes : RouteBuilder
{
    /// <inheritdoc />
    public override void Configure()
    {
        From("file:{{batch.inbox:data/inbox}}?includeExt=csv&move=.done&moveFailed=.failed&initialDelay={{batch.initialDelay:0}}&delay={{batch.delay:2000}}")
            .RouteId("read-batch")
            .ConvertBodyTo(typeof(string))
            .Transform(e => FileSummary.FromCsv(e.Message.GetHeader<string>(CaravanHeaders.FileNameOnly)!, (string)e.Message.Body!))
            .Log("Read ${body.File}: ${body.Lines} lines")
            .Aggregate(
                Constant("batch"),
                AggregationStrategies.GroupedBody(),
                a => a.CompletionFromBatchConsumer(),
                report => report
                    .Transform(e => BatchReport.Render(((IEnumerable<object?>)e.Message.Body!).Cast<FileSummary>()))
                    .ToD("file:{{batch.outbox:data/outbox}}?fileName=report-${date:now:yyyyMMdd-HHmmssSSS}-${exchangeId}.txt")
                    .Log("Wrote ${header.CaravanFileNameProduced}"));
    }
}
