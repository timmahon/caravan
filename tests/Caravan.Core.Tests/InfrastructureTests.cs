using Caravan.Builder;
using Caravan.Components.Log;
using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;
using Caravan.TypeConversion;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests;

public class EndpointUriTests
{
    [Theory]
    [InlineData("direct:start", "direct", "start")]
    [InlineData("seda://queue", "seda", "queue")]
    [InlineData("file:/tmp/in?delete=true", "file", "/tmp/in")]
    public void Parses_scheme_and_path(string uri, string scheme, string path)
    {
        var parsed = EndpointUri.Parse(uri);
        Assert.Equal(scheme, parsed.Scheme);
        Assert.Equal(path, parsed.Path);
    }

    [Fact]
    public void Parses_and_decodes_parameters()
    {
        var parsed = EndpointUri.Parse("log:foo?level=Debug&message=hello%20world&raw=RAW(a+b&c)&last=1");

        Assert.Equal("Debug", parsed.Parameters["level"]);
        Assert.Equal("hello world", parsed.Parameters["message"]);
        Assert.Equal("a+b&c", parsed.Parameters["raw"]);
        Assert.Equal("1", parsed.Parameters["last"]);
    }

    [Fact]
    public void Normalize_sorts_parameters_and_drops_slashes()
    {
        Assert.Equal(EndpointUri.Normalize("seda:a?b=1&a=2"), EndpointUri.Normalize("seda://a?a=2&b=1"));
    }

    [Fact]
    public void Rejects_uri_without_scheme()
    {
        Assert.Throws<ResolveEndpointFailedException>(() => EndpointUri.Parse("nocolon"));
    }
}

public class TypeConverterTests
{
    private readonly TypeConverterRegistry _converter = new();

    [Theory]
    [InlineData("42", typeof(int), 42)]
    [InlineData("42", typeof(long), 42L)]
    [InlineData("true", typeof(bool), true)]
    [InlineData("yes", typeof(bool), true)]
    [InlineData("3.5", typeof(double), 3.5)]
    [InlineData(7, typeof(string), "7")]
    [InlineData(7, typeof(long), 7L)]
    public void Converts_primitives(object value, Type type, object expected)
    {
        Assert.Equal(expected, _converter.Convert(type, null, value));
    }

    [Theory]
    [InlineData("500", 500)]
    [InlineData("5s", 5000)]
    [InlineData("1m30s", 90000)]
    [InlineData("250ms", 250)]
    [InlineData("PT2S", 2000)]
    [InlineData("00:00:03", 3000)]
    public void Converts_durations(string text, int expectedMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), _converter.Convert(typeof(TimeSpan), null, text));
    }

    [Fact]
    public void Converts_bytes_and_streams()
    {
        var bytes = (byte[])_converter.Convert(typeof(byte[]), null, "héllo")!;
        Assert.Equal("héllo", _converter.Convert(typeof(string), null, bytes));
        using var stream = (Stream)_converter.Convert(typeof(Stream), null, "abc")!;
        Assert.Equal("abc", _converter.Convert(typeof(string), null, stream));
    }

    [Fact]
    public void Converts_enums_and_collections()
    {
        Assert.Equal(LogLevel.Warning, _converter.Convert(typeof(LogLevel), null, "warning"));
        Assert.Equal(new[] { 1, 2 }, (int[])_converter.Convert(typeof(int[]), null, new List<object> { "1", 2 })!);
    }

    [Fact]
    public void Custom_converter_takes_precedence()
    {
        _converter.AddConverter<string, Point>(s => new Point(int.Parse(s.Split(',')[0]), int.Parse(s.Split(',')[1])));
        Assert.Equal(new Point(1, 2), _converter.Convert(typeof(Point), null, "1,2"));
    }

    [Fact]
    public void Throws_when_no_conversion()
    {
        Assert.Throws<NoTypeConversionAvailableException>(() => _converter.Convert(typeof(int), null, "not a number"));
        Assert.False(_converter.TryConvert(typeof(Point), null, new object(), out _));
    }

    [Fact]
    public void Null_converts_to_reference_types_only()
    {
        Assert.Null(_converter.Convert(typeof(string), null, null));
        Assert.Throws<NoTypeConversionAvailableException>(() => _converter.Convert(typeof(int), null, null));
    }

    private sealed record Point(int X, int Y);
}

public class ContextInfrastructureTests : ContextTestBase
{
    [Fact]
    public async Task Endpoint_options_are_bound_from_uri()
    {
        await Context.StartAsync();

        var endpoint = Context.GetEndpoint<LogEndpoint>("log:orders?level=Warning&showHeaders=true&maxChars=20");

        Assert.Equal(LogLevel.Warning, endpoint.Level);
        Assert.True(endpoint.ShowHeaders);
        Assert.Equal(20, endpoint.MaxChars);
    }

    [Fact]
    public void Unknown_endpoint_option_is_rejected()
    {
        var ex = Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("log:x?noSuchOption=1"));
        Assert.Contains("noSuchOption", ex.Message);
    }

    [Fact]
    public void Unknown_scheme_is_rejected()
    {
        Assert.Throws<NoSuchEndpointException>(() => Context.GetEndpoint("nope:x"));
    }

    [Fact]
    public void Equivalent_uris_resolve_to_same_endpoint()
    {
        Assert.Same(Context.GetEndpoint("log:a?level=Debug&showAll=true"), Context.GetEndpoint("log://a?showAll=true&level=Debug"));
    }

    [Fact]
    public void Property_placeholders_are_resolved()
    {
        Context.PropertiesSources.Add(new DictionaryPropertiesSource(new Dictionary<string, string?> { ["queue"] = "orders", ["lvl"] = "Debug" }));

        Assert.Equal("direct:orders", Context.ResolvePropertyPlaceholders("direct:{{queue}}"));
        Assert.Equal("x-fallback", Context.ResolvePropertyPlaceholders("x-{{missing:fallback}}"));
        Assert.Throws<ArgumentException>(() => Context.ResolvePropertyPlaceholders("{{missing}}"));
        Assert.Equal(LogLevel.Debug, Context.GetEndpoint<LogEndpoint>("log:{{queue}}?level={{lvl}}").Level);
    }

    [Fact]
    public async Task Endpoint_option_can_reference_registry_bean()
    {
        var formatter = new object();
        Context.Registry.Bind("myBean", formatter);
        Context.AddComponent("bean-holder", new HolderComponent());
        await Context.StartAsync();

        var endpoint = (HolderEndpoint)Context.GetEndpoint("bean-holder:x?bean=#myBean");

        Assert.Same(formatter, endpoint.Bean);
    }

    [Fact]
    public async Task Log_component_writes_formatted_exchange()
    {
        var provider = new CapturingLoggerProvider();
        Context.LoggerFactory = LoggerFactory.Create(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));
        await StartWithRoutes(b => b.From("direct:a").SetHeader("h", Expr.Constant(1)).To("log:test?showHeaders=true"));

        await Template.SendBodyAsync("direct:a", "Hello");

        var line = Assert.Single(provider.Messages);
        Assert.Equal("test", line.Category);
        Assert.Contains("Body: Hello", line.Message);
        Assert.Contains("Headers: {h=1}", line.Message);
        Assert.Contains("BodyType: String", line.Message);
    }

    [Fact]
    public async Task Unit_of_work_runs_synchronizations()
    {
        var completed = new List<string>();
        var failed = new List<string>();
        var sync = new Sync(e => completed.Add(e.ExchangeId), e => failed.Add(e.ExchangeId));
        await StartWithRoutes(b =>
        {
            b.From("direct:ok").Process(e => e.UnitOfWork!.AddSynchronization(sync));
            b.From("direct:fail").Process(e => e.UnitOfWork!.AddSynchronization(sync)).Process(_ => throw new InvalidOperationException());
        });

        await Template.SendBodyAsync("direct:ok", "x");
        await Template.SendAsync("direct:fail", _ => { });

        Assert.Single(completed);
        Assert.Single(failed);
    }

    [Fact]
    public async Task Intercept_strategy_wraps_every_node()
    {
        var visited = new List<string>();
        Context.InterceptStrategies.Add(new RecordingStrategy(visited));
        await StartWithRoutes(b => b.From("direct:a").SetBody(_ => "1").To("mock:x").Process(_ => { }));

        await Template.SendBodyAsync("direct:a", "x");

        Assert.Equal(["setBody", "to", "process"], visited);
    }

    [Fact]
    public async Task Event_notifier_sees_exchange_lifecycle()
    {
        var events = new List<CaravanEventType>();
        Context.EventNotifiers.Add(new CoreRoutingTests.RecordingNotifier(e => events.Add(e.Type)));
        await StartWithRoutes(b => b.From("direct:a").To("mock:x"));

        await Template.SendBodyAsync("direct:a", "x");

        Assert.Contains(CaravanEventType.ContextStarted, events);
        var exchangeEvents = events.Where(e => e is >= CaravanEventType.ExchangeCreated).ToList();
        Assert.Equal([CaravanEventType.ExchangeCreated, CaravanEventType.ExchangeSending, CaravanEventType.ExchangeSent, CaravanEventType.ExchangeCompleted], exchangeEvents);
    }

    [Fact]
    public async Task Mock_reports_unsatisfied_expectations()
    {
        await StartWithRoutes(b => b.From("direct:a").To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("expected");

        await Template.SendBodyAsync("direct:a", "actual");

        var ex = await Assert.ThrowsAsync<Components.Mock.MockAssertionException>(() => mock.AssertIsSatisfiedAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("expected body <expected>", ex.Message);
    }

    [Fact]
    public async Task Mock_can_reply_and_simulate_failures()
    {
        await StartWithRoutes(b => b.From("direct:a").To("mock:result"));
        var mock = Mock("result");
        mock.WhenExchangeReceived(1, _ => throw new TimeoutException());
        mock.ReturnReplyBody(e => "reply");

        Assert.Equal("reply", await Template.RequestBodyAsync<string>("direct:a", "x"));
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:a", "x"));
    }

    [Fact]
    public async Task Exchange_copy_is_independent()
    {
        await Context.StartAsync();
        var exchange = new Exchange(Context);
        exchange.Message.Body = "a";
        exchange.Message.SetHeader("h", 1);
        exchange.SetProperty("p", 2);

        var copy = exchange.CreateCorrelatedCopy();
        copy.Message.SetHeader("h", 99);

        Assert.NotEqual(exchange.ExchangeId, copy.ExchangeId);
        Assert.Equal(exchange.ExchangeId, copy.GetProperty(ExchangePropertyKeys.CorrelationId));
        Assert.Equal(1, exchange.Message.GetHeader("h"));
        Assert.Equal(2, copy.GetProperty("p"));
    }

    [Fact]
    public void Headers_are_case_insensitive_and_removable_by_pattern()
    {
        var exchange = new Exchange(Context);
        exchange.Message.SetHeader("Foo", 1);
        exchange.Message.SetHeader("fooBar", 2);
        exchange.Message.SetHeader("other", 3);

        Assert.Equal(1, exchange.Message.GetHeader("FOO"));
        exchange.Message.RemoveHeaders("foo*", "fooBar");
        Assert.Equal(["fooBar", "other"], exchange.Message.Headers.Keys.Order());
    }

    private sealed class Sync(Action<Exchange> complete, Action<Exchange> fail) : ISynchronization
    {
        public ValueTask OnCompleteAsync(Exchange exchange)
        {
            complete(exchange);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnFailureAsync(Exchange exchange)
        {
            fail(exchange);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingStrategy(List<string> visited) : IInterceptStrategy
    {
        public IProcessor WrapProcessor(RouteContext routeContext, ProcessorDefinition definition, IProcessor target) =>
            new DelegateProcessor(async e =>
            {
                visited.Add(definition.ShortName);
                await target.ProcessAsync(e);
            });
    }

    private sealed class HolderComponent : ComponentBase
    {
        protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) =>
            new HolderEndpoint(uri, this);
    }

    private sealed class HolderEndpoint(string uri, HolderComponent component) : EndpointBase(uri, component, component.Context!)
    {
        public object? Bean { get; set; }

        public override IProducer CreateProducer() => throw new NotSupportedException();

        public override IConsumer CreateConsumer(IProcessor processor) => throw new NotSupportedException();
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<(string Category, LogLevel Level, string Message)> Messages { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (provider.Messages)
            {
                provider.Messages.Add((category, logLevel, formatter(state, exception)));
            }
        }
    }
}
