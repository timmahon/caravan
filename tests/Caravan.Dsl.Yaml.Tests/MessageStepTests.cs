using Caravan.Model;

namespace Caravan.Dsl.Yaml.Tests;

public class MessageStepTests : YamlTestBase
{
    [Fact]
    public async Task To_with_parameters_and_pattern_and_inline_form()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - to: { uri: "log:x", parameters: { showHeaders: true, level: DEBUG } }
                  - to: { uri: mock:a, pattern: InOut }
                  - to: mock:b
            """);
        Mock("a").ExpectedExchangePattern(ExchangePattern.InOut);
        Mock("b").ExpectedBodiesReceived("x");

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("a").AssertIsSatisfiedAsync();
        await Mock("b").AssertIsSatisfiedAsync();
        Assert.Contains(Context.Endpoints, e => e.EndpointUri.Contains("showHeaders=true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToD_resolves_the_uri_per_exchange()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - toD: { uri: "mock:${header.target}", cacheSize: 5 }
            """);
        Mock("one").ExpectedBodiesReceived("1");
        Mock("two").ExpectedBodiesReceived("2");

        await Template.SendBodyAndHeaderAsync("direct:start", "1", "target", "one");
        await Template.SendBodyAndHeaderAsync("direct:start", "2", "target", "two");

        await Mock("one").AssertIsSatisfiedAsync();
        await Mock("two").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task InOnly_and_inOut_set_the_pattern()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - inOut: mock:a
                  - inOnly: { uri: mock:b }
            """);
        Mock("a").ExpectedExchangePattern(ExchangePattern.InOut);
        Mock("b").ExpectedExchangePattern(ExchangePattern.InOnly);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("a").AssertIsSatisfiedAsync();
        await Mock("b").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Process_calls_a_registry_processor()
    {
        Context.Registry.Bind("upper", new UpperCaseProcessor());
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - process: { ref: upper }
                  - process: upper
                  - to: mock:result
            """);
        Mock("result").ExpectedBodiesReceived("ABC");

        await Template.SendBodyAsync("direct:start", "abc");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Bean_by_ref_and_by_type()
    {
        Context.Registry.Bind("greeter", new Greeter { Greeting = "Hi" });
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - bean: { ref: greeter, method: Greet }
                  - bean: { beanType: Caravan.Dsl.Yaml.Tests.Greeter, method: Shout }
                  - to: mock:result
            """);
        Mock("result").ExpectedBodiesReceived("HI BOB!");

        await Template.SendBodyAsync("direct:start", "Bob");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task SetBody_transform_setHeader_setProperty_and_setHeaders()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - setProperty: { name: original, simple: "${body}" }
                  - setBody: { simple: "${body}-set" }
                  - transform: { expression: { simple: "${body}-transformed" } }
                  - setHeader: { name: count, simple: { expression: "5", resultType: int } }
                  - setHeaders:
                      headers:
                        - name: a
                          constant: A
                        - name: b
                          simple: "${header.a}B"
                  - to: mock:result
            """);
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("x-set-transformed");
        mock.ExpectedHeaderReceived("count", 5);
        mock.ExpectedHeaderReceived("b", "AB");
        mock.ExpectedPropertyReceived("original", "x");

        await Template.SendBodyAsync("direct:start", "x");

        await mock.AssertIsSatisfiedAsync();
        Assert.IsType<int>(mock.ReceivedExchanges[0].Message.GetHeader("count"));
    }

    [Fact]
    public async Task Remove_headers_and_properties()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - setProperty: { name: p1, constant: "1" }
                  - setProperty: { name: p2, constant: "2" }
                  - setProperty: { name: keep, constant: "k" }
                  - removeHeader: { name: h1 }
                  - removeHeaders: { pattern: "x*", excludePattern: xKeep }
                  - removeProperty: p1
                  - removeProperties: { pattern: "p*" }
                  - to: mock:result
            """);
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);

        await Template.SendBodyAndHeadersAsync("direct:start", "x", new Dictionary<string, object?> { ["h1"] = 1, ["xA"] = 2, ["xKeep"] = 3, ["other"] = 4 });

        await mock.AssertIsSatisfiedAsync();
        var exchange = mock.ReceivedExchanges[0];
        Assert.False(exchange.Message.HasHeader("h1"));
        Assert.False(exchange.Message.HasHeader("xA"));
        Assert.True(exchange.Message.HasHeader("xKeep"));
        Assert.True(exchange.Message.HasHeader("other"));
        Assert.Null(exchange.GetProperty("p1"));
        Assert.Null(exchange.GetProperty("p2"));
        Assert.Equal("k", exchange.GetProperty("keep"));
    }

    [Fact]
    public async Task ConvertBodyTo_and_convertHeaderTo_accept_java_and_dotnet_type_names()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - convertBodyTo: java.lang.Integer
                  - convertHeaderTo: { name: flag, type: bool, toName: flagged }
                  - to: mock:a
                  - convertBodyTo: { type: String }
                  - to: mock:b
            """);
        Mock("a").ExpectedBodiesReceived(42);
        Mock("a").ExpectedHeaderReceived("flagged", true);
        Mock("b").ExpectedBodiesReceived("42");

        await Template.SendBodyAndHeaderAsync("direct:start", "42", "flag", "true");

        await Mock("a").AssertIsSatisfiedAsync();
        await Mock("b").AssertIsSatisfiedAsync();
        Assert.IsType<int>(Mock("a").ReceivedExchanges[0].Message.Body);
    }

    [Fact]
    public async Task Log_with_level_and_name()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - log: { message: "Got ${body}", loggingLevel: DEBUG, logName: my.logger }
                  - log: "Inline ${body}"
                  - to: mock:result
            """);
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        var log = Assert.IsType<LogDefinition>(Context.RouteDefinitions[0].Outputs[0]);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, log.LoggingLevel);
        Assert.Equal("my.logger", log.LogName);
    }

    [Fact]
    public async Task SetExchangePattern_changes_the_pattern()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - setExchangePattern: InOut
                  - to: mock:result
            """);
        Mock("result").ExpectedExchangePattern(ExchangePattern.InOut);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Script_runs_for_side_effects_only()
    {
        Context.Registry.Bind("greeter", new Greeter());
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - script: { simple: "${exchangeProperty.counter}" }
                  - script: { method: { ref: greeter, method: Init } }
                  - to: mock:result
            """);
        Mock("result").ExpectedBodiesReceived("x");

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.True(((Greeter)Context.Registry.Lookup("greeter")!).Initialized);
    }

    [Fact]
    public async Task Sort_with_and_without_comparator()
    {
        Context.Registry.Bind("descending", new DescendingComparer());
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - sort: { tokenize: "," }
                  - to: mock:a
                  - sort: { comparator: descending }
                  - to: mock:b
            """);
        Mock("a").ExpectedMessageCount(1);
        Mock("b").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "b,c,a");

        await Mock("a").AssertIsSatisfiedAsync();
        await Mock("b").AssertIsSatisfiedAsync();
        Assert.Equal(["a", "b", "c"], (IEnumerable<object?>)Mock("a").ReceivedExchanges[0].Message.Body!);
        Assert.Equal(["c", "b", "a"], (IEnumerable<object?>)Mock("b").ReceivedExchanges[0].Message.Body!);
    }

    [Fact]
    public async Task Validate_fails_with_a_predicate_validation_exception()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - validate: { simple: "${body} contains 'ok'" }
                  - to: mock:result
            """);
        Mock("result").ExpectedBodiesReceived("ok!");

        await Template.SendBodyAsync("direct:start", "ok!");
        var failed = await Template.SendAsync("direct:start", e => e.Message.Body = "bad");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.IsType<PredicateValidationException>(failed.Exception);
    }

    [Fact]
    public async Task Marshal_and_unmarshal_with_data_format_options()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - unmarshal: { json: { unmarshalType: Caravan.Dsl.Yaml.Tests.OrderLine, library: Jackson } }
                  - setHeader: { name: item, simple: "${body.Item}" }
                  - marshal: { json: {} }
                  - convertBodyTo: String
                  - to: mock:json
                  - marshal: { base64: { lineLength: 0 } }
                  - unmarshal: { custom: base64 }
                  - to: mock:roundtrip
            """);
        Mock("json").ExpectedHeaderReceived("item", "pen");
        Mock("roundtrip").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", """{"item":"pen","quantity":2}""");

        await Mock("json").AssertIsSatisfiedAsync();
        await Mock("roundtrip").AssertIsSatisfiedAsync();
        Assert.Equal("""{"item":"pen","quantity":2}""", Mock("json").ReceivedExchanges[0].Message.Body);
        Assert.Equal("""{"item":"pen","quantity":2}""", System.Text.Encoding.UTF8.GetString((byte[])Mock("roundtrip").ReceivedExchanges[0].Message.Body!));
    }

    [Fact]
    public async Task Stop_ends_routing()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - to: mock:before
                  - stop: {}
                  - to: mock:after
            """);
        Mock("before").ExpectedMessageCount(1);
        Mock("after").ExpectedMessageCount(0);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("before").AssertIsSatisfiedAsync();
        await Mock("after").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Id_description_and_disabled_apply_to_every_step()
    {
        await StartAsync("""
            - route:
                id: r
                from:
                  uri: direct:start
                  steps:
                    - setBody: { id: set, description: Sets the body, constant: changed }
                    - to: { id: skipped, uri: mock:skipped, disabled: true }
                    - filter:
                        id: f
                        simple: "${body} == 'changed'"
                        steps:
                          - to: { id: inner, uri: mock:result }
            """);
        Mock("result").ExpectedBodiesReceived("changed");
        Mock("skipped").ExpectedMessageCount(0);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        await Mock("skipped").AssertIsSatisfiedAsync();
        var nodes = Context.RouteDefinitions[0].AllNodes().ToList();
        Assert.Equal(["set", "skipped", "f", "inner"], nodes.Select(n => n.Id));
        Assert.Equal("Sets the body", nodes[0].Description);
        Assert.True(nodes[1].Disabled);
    }

    [Fact]
    public async Task Kebab_case_step_and_option_names_are_accepted()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - set-header: { name: h, constant: v }
                  - remove-header: gone
                  - convert-body-to: { type: int, mandatory: true }
                  - to-d: { uri: "mock:${header.h}", ignore-invalid-endpoint: false }
            """);
        Mock("v").ExpectedBodiesReceived(7);

        await Template.SendBodyAndHeaderAsync("direct:start", "7", "gone", 1);

        await Mock("v").AssertIsSatisfiedAsync();
    }
}
