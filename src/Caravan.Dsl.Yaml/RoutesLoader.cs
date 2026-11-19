using System.Collections.Concurrent;
using Caravan.Model;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Dsl.Yaml;

/// <summary>
/// Loads YAML DSL files matching a path, directory or glob (<c>routes/*.yaml</c>, <c>routes/**/*.yaml</c>) into a context, and optionally
/// watches them to reload changed routes (<see cref="RoutesLoaderOptions.Watch"/>, Camel's route reload).
/// </summary>
/// <remarks>
/// Files are loaded in ordinal order of their paths. The loader remembers which routes, route templates and route configurations each file
/// added (by comparing the context's definitions before and after, so other code should not add routes concurrently with a load). When watching,
/// the loader is a service of the context: the watchers start with the context (or at once if it runs) and stop with it. A change is
/// reloaded once the file has been quiet for <see cref="RoutesLoaderOptions.DebounceInterval"/> (on the context's clock): a file that does not
/// parse keeps its routes; otherwise its routes are stopped and removed, with its templates and configurations, then the file is added again.
/// </remarks>
public sealed class RoutesLoader : ServiceSupport, IAsyncDisposable
{
    private readonly ICaravanContext _context;
    private readonly RoutesLoaderOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<FilePattern> _patterns = [];
    private readonly Dictionary<string, FileState> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ITimer> _pending = new(StringComparer.Ordinal);
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly List<Task> _reloads = [];

    /// <summary>Creates a loader for <paramref name="context"/>.</summary>
    /// <param name="context">The context the routes are added to.</param>
    /// <param name="options">The options (default: no watching).</param>
    public RoutesLoader(ICaravanContext context, RoutesLoaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _options = options ?? new RoutesLoaderOptions();
        _logger = context.LoggerFactory.CreateLogger(typeof(RoutesLoader).FullName!);
    }

    /// <summary>Raised after a watched file was reloaded (or failed to reload), on a background thread.</summary>
    public event EventHandler<RoutesReloadedEventArgs>? Reloaded;

    /// <summary>The directory relative patterns are resolved against (default: the current directory).</summary>
    public string BaseDirectory { get; set; } = Directory.GetCurrentDirectory();

    /// <summary>The route ids loaded per file (full paths).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> LoadedRoutes
    {
        get
        {
            lock (_files)
            {
                return _files.ToDictionary(f => f.Key, f => (IReadOnlyList<string>)[.. f.Value.RouteIds], StringComparer.Ordinal);
            }
        }
    }

    /// <summary>Loads the files matching <paramref name="pattern"/>; when watching, also watches them (and new matching files).</summary>
    /// <param name="pattern">A file, a directory (its .yaml/.yml files) or a glob.</param>
    /// <param name="cancellationToken">Cancels starting the routes on a running context.</param>
    /// <returns>The ids of the routes loaded.</returns>
    /// <exception cref="YamlRouteDefinitionException">A file is invalid.</exception>
    public async Task<IReadOnlyList<string>> LoadAsync(string pattern, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var filePattern = FilePattern.Parse(pattern, BaseDirectory);
        var ids = new List<string>();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _patterns.Add(filePattern);
            foreach (var file in filePattern.Files())
            {
                if (!Known(file))
                {
                    ids.AddRange(await AddFileAsync(file, YamlRoutesBuilder.FromFile(file), cancellationToken).ConfigureAwait(false));
                }
            }

            if (_options.Watch && IsRunAllowed)
            {
                Watch(filePattern);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (_options.Watch)
        {
            await _context.AddServiceAsync(this, cancellationToken).ConfigureAwait(false);
        }

        return ids;
    }

    /// <summary>Loads the files matching <paramref name="pattern"/> (see <see cref="LoadAsync"/>), synchronously.</summary>
    /// <param name="pattern">A file, a directory or a glob.</param>
    /// <returns>The ids of the routes loaded.</returns>
    public IReadOnlyList<string> Load(string pattern) => LoadAsync(pattern).GetAwaiter().GetResult();

    /// <summary>Reloads one file now (as a watcher would after a change).</summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">Cancels the reload.</param>
    /// <returns>What happened.</returns>
    public async Task<RoutesReloadedEventArgs> ReloadAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path, BaseDirectory);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        RoutesReloadedEventArgs result;
        try
        {
            result = await ReloadLockedAsync(full, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        Reloaded?.Invoke(this, result);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lock.Dispose();
    }

    /// <inheritdoc />
    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var pattern in _patterns)
            {
                Watch(pattern);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        List<Task> reloads;
        lock (_watchers)
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            reloads = [.. _reloads];
        }

        foreach (var timer in _pending.Values)
        {
            timer.Dispose();
        }

        _pending.Clear();
        await Task.WhenAll(reloads).ConfigureAwait(false);
    }

    private bool Known(string file)
    {
        lock (_files)
        {
            return _files.ContainsKey(file);
        }
    }

    private void Watch(FilePattern pattern)
    {
        if (!Directory.Exists(pattern.Directory))
        {
            _logger.LogWarning("Cannot watch {Directory}: it does not exist", pattern.Directory);
            return;
        }

        var watcher = new FileSystemWatcher(pattern.Directory, pattern.Filter ?? "*")
        {
            IncludeSubdirectories = pattern.Recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        watcher.Changed += (_, e) => OnChange(e.FullPath);
        watcher.Created += (_, e) => OnChange(e.FullPath);
        watcher.Deleted += (_, e) => OnChange(e.FullPath);
        watcher.Renamed += (_, e) =>
        {
            OnChange(e.OldFullPath);
            OnChange(e.FullPath);
        };
        watcher.Error += (_, e) => _logger.LogWarning(e.GetException(), "File watcher error in {Directory}", pattern.Directory);
        watcher.EnableRaisingEvents = true;
        lock (_watchers)
        {
            _watchers.Add(watcher);
        }
    }

    private void OnChange(string path)
    {
        if (!_patterns.ToList().Any(p => p.Matches(path)) && !Known(path))
        {
            return;
        }

        // Debounce: (re)start the file's timer; the reload runs once the file is quiet.
        var timer = _context.TimeProvider.CreateTimer(_ => StartReload(path), null, _options.DebounceInterval, Timeout.InfiniteTimeSpan);
        _pending.AddOrUpdate(path, timer, (_, previous) =>
        {
            previous.Dispose();
            return timer;
        });
    }

    private void StartReload(string path)
    {
        lock (_watchers)
        {
            if (!IsRunAllowed)
            {
                return;
            }

            var reload = Task.Run(() => ReloadAsync(path));
            _reloads.Add(reload);
            _ = reload.ContinueWith(
                t =>
                {
                    lock (_watchers)
                    {
                        _reloads.Remove(t);
                    }

                    if (t.Exception is { } e)
                    {
                        _logger.LogError(e.InnerException, "Reloading {Path} failed", path);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task<RoutesReloadedEventArgs> ReloadLockedAsync(string path, CancellationToken cancellationToken)
    {
        YamlRoutesBuilder? builder = null;
        if (File.Exists(path))
        {
            try
            {
                builder = YamlRoutesBuilder.FromFile(path);
            }
            catch (Exception e) when (e is YamlRouteDefinitionException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(e, "Cannot reload {Path}; its routes are kept", path);
                return new RoutesReloadedEventArgs(path, RouteIdsOf(path), e);
            }
        }

        FileState? previous;
        lock (_files)
        {
            _files.Remove(path, out previous);
        }

        if (previous is not null)
        {
            foreach (var routeId in previous.RouteIds)
            {
                if (_context.GetRoute(routeId) is not null)
                {
                    await _context.StopRouteAsync(routeId, cancellationToken).ConfigureAwait(false);
                }

                await _context.RemoveRouteAsync(routeId, cancellationToken).ConfigureAwait(false);
            }

            foreach (var template in previous.Templates)
            {
                _context.RemoveRouteTemplateDefinition(template);
            }

            foreach (var configuration in previous.Configurations)
            {
                _context.RemoveRouteConfiguration(configuration);
            }
        }

        if (builder is null)
        {
            _logger.LogInformation("Removed the routes of deleted file {Path}: {Routes}", path, previous?.RouteIds ?? []);
            return new RoutesReloadedEventArgs(path, [], null);
        }

        try
        {
            var ids = await AddFileAsync(path, builder, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Reloaded {Path}: {Routes}", path, ids);
            return new RoutesReloadedEventArgs(path, ids, null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogError(e, "Cannot reload {Path}", path);
            return new RoutesReloadedEventArgs(path, RouteIdsOf(path), e);
        }
    }

    private IReadOnlyList<string> RouteIdsOf(string path)
    {
        lock (_files)
        {
            return _files.TryGetValue(path, out var state) ? [.. state.RouteIds] : [];
        }
    }

    private async Task<List<string>> AddFileAsync(string path, YamlRoutesBuilder builder, CancellationToken cancellationToken)
    {
        var routesBefore = _context.RouteDefinitions.ToHashSet(ReferenceEqualityComparer.Instance);
        var templatesBefore = _context.RouteTemplateDefinitions.ToHashSet(ReferenceEqualityComparer.Instance);
        var configurationsBefore = _context.RouteConfigurationDefinitions.ToHashSet(ReferenceEqualityComparer.Instance);
        var state = new FileState();
        try
        {
            await _context.AddRoutesAsync(builder, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Record what was added even when starting a route failed, so that a later reload removes it.
            state.RouteIds.AddRange(_context.RouteDefinitions.Where(r => !routesBefore.Contains(r)).Select(r => r.Id!));
            state.Templates.AddRange(_context.RouteTemplateDefinitions.Where(t => !templatesBefore.Contains(t)).Select(t => t.Id));
            state.Configurations.AddRange(_context.RouteConfigurationDefinitions.Where(c => !configurationsBefore.Contains(c)));
            lock (_files)
            {
                _files[path] = state;
            }
        }

        return [.. state.RouteIds];
    }

    /// <summary>What a file added to the context.</summary>
    private sealed class FileState
    {
        public List<string> RouteIds { get; } = [];

        public List<string> Templates { get; } = [];

        public List<RouteConfigurationDefinition> Configurations { get; } = [];
    }
}
