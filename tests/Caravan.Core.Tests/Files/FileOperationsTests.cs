using Caravan.Components.File;

namespace Caravan.Core.Tests;

public class FileOperationsTests : FileTestBase
{
    [Fact]
    public async Task Listing_skips_files_deleted_while_the_directory_is_enumerated()
    {
        // Another consumer creating and deleting marker files in the same directory must not make
        // the listing fail: a file can vanish between being enumerated and having its size read.
        for (var i = 0; i < 50; i++)
        {
            WriteFile($"in/keep{i}.txt", "x");
        }

        var operations = new FileOperations(Endpoint(From("in")));
        using var stop = new CancellationTokenSource();
        var churn = Task.Run(() =>
        {
            var n = 0;
            while (!stop.IsCancellationRequested)
            {
                var path = PathOf("in", $"churn{n++ % 20}.caravanLock");
                File.WriteAllText(path, string.Empty);
                File.Delete(path);
            }
        });

        try
        {
            for (var i = 0; i < 2000; i++)
            {
                var files = operations.ListFiles(PathOf("in"));
                Assert.True(files.Count(f => f.FileNameOnly.StartsWith("keep", StringComparison.Ordinal)) == 50);
            }
        }
        finally
        {
            await stop.CancelAsync();
            await churn;
        }
    }
}
