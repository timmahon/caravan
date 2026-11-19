namespace Caravan.Dsl.Yaml;

/// <summary>A file watched by a <see cref="RoutesLoader"/> was reloaded (or failed to).</summary>
/// <param name="path">The full path of the file.</param>
/// <param name="routeIds">The ids of the routes now loaded from the file (empty when it was deleted or failed).</param>
/// <param name="error">The error when the file could not be loaded; its previous routes are then kept if the file could not be parsed.</param>
public sealed class RoutesReloadedEventArgs(string path, IReadOnlyList<string> routeIds, Exception? error) : EventArgs
{
    /// <summary>The full path of the file.</summary>
    public string Path { get; } = path;

    /// <summary>The ids of the routes now loaded from the file.</summary>
    public IReadOnlyList<string> RouteIds { get; } = routeIds;

    /// <summary>The error, or null when the reload succeeded.</summary>
    public Exception? Error { get; } = error;
}
