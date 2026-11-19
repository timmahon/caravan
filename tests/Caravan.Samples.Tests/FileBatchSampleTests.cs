using Caravan.Samples.FileBatch;

namespace Caravan.Samples.Tests;

/// <summary>The file batch sample's route reads the files of a poll and writes one report.</summary>
public sealed class FileBatchSampleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "caravan-batch-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task The_files_of_a_poll_become_one_report()
    {
        var inbox = Directory.CreateDirectory(Path.Combine(_root, "in")).FullName;
        var outbox = Path.Combine(_root, "out");
        await File.WriteAllTextAsync(Path.Combine(inbox, "a.csv"), "customer,amount\nann,10.5\nbob,2\n");
        await File.WriteAllTextAsync(Path.Combine(inbox, "b.csv"), "carl,7.25\n");
        await File.WriteAllTextAsync(Path.Combine(inbox, "ignored.txt"), "x");

        await using var context = new CaravanContext();
        var properties = context.PropertiesComponent.InitialProperties;
        properties["batch.inbox"] = inbox;
        properties["batch.outbox"] = outbox;
        properties["batch.delay"] = "60000";
        context.AddRoutes(new BatchRoutes());

        await context.StartAsync();
        var report = await WaitForReportAsync(outbox);

        Assert.Equal("a.csv: 2 lines, total 12.50\nb.csv: 1 lines, total 7.25\nTOTAL: 2 files, 3 lines, 19.75", report.ReplaceLineEndings("\n"));
        // Each file is moved once its own exchange completes; the report may be written while the last file
        // of the poll (in directory order, which is not guaranteed) is still in flight.
        await WaitUntilAsync(() => File.Exists(Path.Combine(inbox, ".done", "a.csv")) && File.Exists(Path.Combine(inbox, ".done", "b.csv")));
        Assert.True(File.Exists(Path.Combine(inbox, "ignored.txt")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The processed files were not moved to .done");
            }

            await Task.Delay(10);
        }
    }

    private static async Task<string> WaitForReportAsync(string outbox)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            // Read once the producer finished writing (the TOTAL line comes last).
            if (Directory.Exists(outbox) && Directory.GetFiles(outbox, "report-*.txt") is [var file])
            {
                try
                {
                    var text = await File.ReadAllTextAsync(file);
                    if (text.Contains("TOTAL", StringComparison.Ordinal))
                    {
                        return text;
                    }
                }
                catch (IOException)
                {
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("No report was written");
            }

            await Task.Delay(10);
        }
    }
}
