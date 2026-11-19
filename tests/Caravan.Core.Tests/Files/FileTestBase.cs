using Caravan.Components.File;

namespace Caravan.Core.Tests;

/// <summary>
/// Base class of the file component tests: a fresh temporary directory per test (deleted afterwards) and a fake clock. Consumer URIs poll at
/// once (<c>initialDelay=0</c>) then every second of fake time, so <see cref="FirstPollAsync"/> and <see cref="NextPollAsync"/> wait until a poll
/// (and the completion of its files) is done.
/// </summary>
public abstract class FileTestBase : ContextTestBase
{
    protected FileTestBase()
    {
        Context.TimeProvider = Time;
        Dir = Path.Combine(Path.GetTempPath(), "caravan-file-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
    }

    protected TestTimeProvider Time { get; } = new();

    /// <summary>The test's own directory (absolute).</summary>
    protected string Dir { get; }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        for (var attempt = 0; attempt < 3 && Directory.Exists(Dir); attempt++)
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch (IOException)
            {
                await Task.Delay(50);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(50);
            }
        }
    }

    /// <summary>A path below the test directory.</summary>
    protected string PathOf(params string[] parts) => Path.Combine([Dir, .. parts]);

    /// <summary>A consumer URI polling <paramref name="sub"/> at once, then every second of fake time.</summary>
    protected string From(string sub, string? options = null) =>
        $"file:{PathOf(sub)}?initialDelay=0&delay=1000" + (string.IsNullOrEmpty(options) ? string.Empty : "&" + options);

    /// <summary>A producer URI writing to <paramref name="sub"/>.</summary>
    protected string To(string sub, string? options = null) =>
        $"file:{PathOf(sub)}" + (string.IsNullOrEmpty(options) ? string.Empty : "?" + options);

    protected string WriteFile(string relative, string content, DateTimeOffset? modified = null)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modified is { } m)
        {
            File.SetLastWriteTimeUtc(path, m.UtcDateTime);
        }

        return path;
    }

    protected string ReadFile(string relative) => File.ReadAllText(PathOf(relative));

    protected bool Exists(string relative) => File.Exists(PathOf(relative));

    /// <summary>Waits until the first poll (started with the route) is done.</summary>
    protected Task FirstPollAsync(int consumers = 1) => Time.WaitForTimersAsync(consumers);

    /// <summary>Runs the next poll and waits until it is done.</summary>
    protected Task NextPollAsync() => Time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));

    /// <summary>The names of the files in <paramref name="sub"/> (relative, sorted, '/' separated).</summary>
    protected string[] FilesIn(string sub)
    {
        var root = PathOf(sub);
        return Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal)]
            : [];
    }

    protected FileEndpoint Endpoint(string uri) => Context.GetEndpoint<FileEndpoint>(uri);
}
