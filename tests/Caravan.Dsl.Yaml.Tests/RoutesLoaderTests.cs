namespace Caravan.Dsl.Yaml.Tests;

public sealed class RoutesLoaderTests : YamlTestBase
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "caravan-yaml-" + Guid.NewGuid().ToString("N"));

    public override Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "sub"));
        return base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    private string Write(string name, string routeId, string target)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, $$"""
            - route:
                id: {{routeId}}
                from:
                  uri: direct:{{routeId}}
                  steps: [ { to: "mock:{{target}}" } ]
            """);
        return path;
    }

    [Fact]
    public async Task Loads_files_matching_a_glob_a_directory_or_a_path()
    {
        Write("a.yaml", "a", "a");
        Write("b.yml", "b", "b");
        Write("ignored.txt", "ignored", "ignored");
        Write(Path.Combine("sub", "c.yaml"), "c", "c");
        var loader = new RoutesLoader(Context) { BaseDirectory = _directory };

        Assert.Equal(["a"], loader.Load("*.yaml"));
        Assert.Equal(["b"], loader.Load(_directory));
        Assert.Equal(["c"], loader.Load("**/*.yaml"));
        Assert.Empty(loader.Load("sub/c.yaml"));
        await Context.StartAsync();

        Assert.Equal(["a", "b", "c"], Context.RouteDefinitions.Select(r => r.Id).Order());
        Assert.Equal(3, loader.LoadedRoutes.Count);
        Mock("c").ExpectedMessageCount(1);
        await Template.SendBodyAsync("direct:c", "x");
        await Mock("c").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task ReloadAsync_replaces_the_routes_of_a_file()
    {
        var path = Write("r.yaml", "r", "v1");
        await using var loader = new RoutesLoader(Context) { BaseDirectory = _directory };
        await loader.LoadAsync("r.yaml");
        await Context.StartAsync();

        Write("r.yaml", "r", "v2");
        var result = await loader.ReloadAsync(path);

        Assert.Null(result.Error);
        Assert.Equal(["r"], result.RouteIds);
        Mock("v1").ExpectedMessageCount(0);
        Mock("v2").ExpectedMessageCount(1);
        await Template.SendBodyAsync("direct:r", "x");
        await Mock("v1").AssertIsSatisfiedAsync();
        await Mock("v2").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Watching_reloads_changed_files_keeps_broken_ones_and_removes_deleted_ones()
    {
        var path = Write("w.yaml", "w", "first");
        await using var loader = new RoutesLoader(Context, new RoutesLoaderOptions { Watch = true, DebounceInterval = TimeSpan.FromMilliseconds(200) })
        {
            BaseDirectory = _directory,
        };
        await loader.LoadAsync("*.yaml");
        await Context.StartAsync();

        // A change: the route now sends to mock:second.
        var reloaded = WaitFor(loader, e => e.Path == path && e.Error is null && e.RouteIds.SequenceEqual(["w"]));
        Write("w.yaml", "w", "second");
        await reloaded;
        await SendAndExpect("direct:w", "second");

        // A broken file keeps the routes.
        var failed = WaitFor(loader, e => e.Path == path && e.Error is YamlRouteDefinitionException);
        File.WriteAllText(path, "- route: [ broken");
        await failed;
        await SendAndExpect("direct:w", "second");

        // A new file is loaded.
        var other = Path.Combine(_directory, "new.yaml");
        var added = WaitFor(loader, e => e.Path == other && e.RouteIds.SequenceEqual(["n"]));
        Write("new.yaml", "n", "new");
        await added;
        await SendAndExpect("direct:n", "new");

        // A deleted file's routes are removed.
        var removed = WaitFor(loader, e => e.Path == other && e.Error is null && e.RouteIds.Count == 0);
        File.Delete(other);
        await removed;
        Assert.Null(Context.GetRoute("n"));
        Assert.NotNull(Context.GetRoute("w"));
    }

    private async Task SendAndExpect(string uri, string mock)
    {
        Mock(mock).Reset();
        Mock(mock).ExpectedMessageCount(1);
        await Template.SendBodyAsync(uri, "x");
        await Mock(mock).AssertIsSatisfiedAsync();
    }

    private static Task WaitFor(RoutesLoader loader, Func<RoutesReloadedEventArgs, bool> condition)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, RoutesReloadedEventArgs e)
        {
            if (condition(e))
            {
                loader.Reloaded -= Handler;
                done.TrySetResult();
            }
        }

        loader.Reloaded += Handler;
        return done.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
