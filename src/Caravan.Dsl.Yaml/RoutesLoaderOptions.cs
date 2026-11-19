namespace Caravan.Dsl.Yaml;

/// <summary>Options of a <see cref="RoutesLoader"/>.</summary>
public sealed class RoutesLoaderOptions
{
    /// <summary>
    /// Watches the loaded files (and new files matching the patterns) and reloads the routes of a file when it changes: they are stopped and
    /// removed, with the file's templates and route configurations, then added again (Camel's route reload). A deleted file's routes are
    /// removed. Default false.
    /// </summary>
    public bool Watch { get; set; }

    /// <summary>How long a file must be quiet before it is reloaded (editors write files in several steps). Default 500 ms.</summary>
    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(500);
}
