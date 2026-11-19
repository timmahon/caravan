namespace Caravan.Dsl.Yaml.Tests;

public class RoutingStepTests : YamlTestBase
{
    [Fact]
    public async Task Filter_runs_its_steps_for_matching_exchanges()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - filter:
                      expression: { simple: "${header.vip} == true" }
                      steps: [ { to: mock:vip } ]
                  - to: mock:all
            """);
        Mock("vip").ExpectedBodiesReceived("a");
        Mock("all").ExpectedBodiesReceived("a", "b");

        await Template.SendBodyAndHeaderAsync("direct:start", "a", "vip", true);
        await Template.SendBodyAndHeaderAsync("direct:start", "b", "vip", false);

        await Mock("vip").AssertIsSatisfiedAsync();
        await Mock("all").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Choice_takes_the_first_matching_when()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - choice:
                      when:
                        - header: gold
                          steps: [ { to: mock:gold } ]
                        - simple: "${body} > 10"
                          steps: [ { to: mock:big } ]
                      otherwise:
                        steps: [ { to: mock:other } ]
            """);
        Mock("gold").ExpectedBodiesReceived(1);
        Mock("big").ExpectedBodiesReceived(20);
        Mock("other").ExpectedBodiesReceived(2);

        await Template.SendBodyAndHeaderAsync("direct:start", 1, "gold", true);
        await Template.SendBodyAsync("direct:start", 20);
        await Template.SendBodyAsync("direct:start", 2);

        await Mock("gold").AssertIsSatisfiedAsync();
        await Mock("big").AssertIsSatisfiedAsync();
        await Mock("other").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Split_with_tokenize_and_aggregation_strategy()
    {
        Context.Registry.Bind("joined", AggregationStrategies.String("+"));
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - split:
                      tokenize: ","
                      aggregationStrategy: joined
                      parallelProcessing: true
                      stopOnException: true
                      steps:
                        - transform: { simple: "${body.ToUpper()}" }
                        - to: mock:part
                  - to: mock:result
            """);
        Mock("part").ExpectedBodiesReceivedInAnyOrder("A", "B", "C");
        Mock("result").ExpectedBodiesReceived("A+B+C");

        await Template.SendBodyAsync("direct:start", "a,b,c");

        await Mock("part").AssertIsSatisfiedAsync();
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Split_with_long_form_tokenize_options()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - split:
                      tokenize: { token: "\n", skipFirst: true }
                      streaming: true
                      steps: [ { to: mock:line } ]
            """);
        Mock("line").ExpectedBodiesReceived("1", "2");

        await Template.SendBodyAsync("direct:start", "header\n1\n2");

        await Mock("line").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Multicast_sends_to_each_step_and_aggregates()
    {
        Context.Registry.Bind("collect", AggregationStrategies.GroupedBody());
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - multicast:
                      aggregationStrategy: collect
                      parallel-processing: true
                      steps:
                        - pipeline:
                            steps:
                              - setBody: { simple: "${body}1" }
                              - to: mock:a
                        - pipeline:
                            steps:
                              - setBody: { simple: "${body}2" }
                              - to: mock:b
                  - to: mock:result
            """);
        Mock("a").ExpectedBodiesReceived("x1");
        Mock("b").ExpectedBodiesReceived("x2");
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("a").AssertIsSatisfiedAsync();
        await Mock("b").AssertIsSatisfiedAsync();
        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(["x1", "x2"], ((IEnumerable<object?>)Mock("result").ReceivedExchanges[0].Message.Body!).Cast<string>());
    }

    [Fact]
    public async Task RecipientList_routingSlip_and_dynamicRouter()
    {
        Context.Registry.Bind("router", new Router());
        await StartAsync("""
            - from:
                uri: direct:list
                steps:
                  - recipientList: { header: targets, delimiter: ";" }
            - from:
                uri: direct:slip
                steps:
                  - routingSlip: { header: slip, uriDelimiter: "#" }
            - from:
                uri: direct:dynamic
                steps:
                  - dynamicRouter: { method: { ref: router, method: Next } }
            """);
        Mock("a").ExpectedBodiesReceived("list", "slip", "dynamic");
        Mock("b").ExpectedBodiesReceived("list", "slip", "dynamic");

        await Template.SendBodyAndHeaderAsync("direct:list", "list", "targets", "mock:a;mock:b");
        await Template.SendBodyAndHeaderAsync("direct:slip", "slip", "slip", "mock:a#mock:b");
        await Template.SendBodyAsync("direct:dynamic", "dynamic");

        await Mock("a").AssertIsSatisfiedAsync();
        await Mock("b").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task WireTap_sends_a_copy()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - wireTap: { uri: "mock:${header.tap}" }
                  - setBody: { constant: changed }
                  - to: mock:result
            """);
        Mock("tapped").ExpectedBodiesReceived("x");
        Mock("result").ExpectedBodiesReceived("changed");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "tap", "tapped");

        await Mock("tapped").AssertIsSatisfiedAsync();
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Enrich_and_pollEnrich()
    {
        Context.Registry.Bind("merge", AggregationStrategies.From((original, resource) =>
        {
            original!.Message.Body = $"{original.Message.Body}+{resource.Message.Body}";
            return original;
        }));
        await StartAsync("""
            - from:
                uri: direct:resource
                steps:
                  - setBody: { constant: resource }
            - from:
                uri: direct:enrich
                steps:
                  - enrich: { constant: "direct:resource", aggregationStrategy: merge }
                  - to: mock:enriched
            - from:
                uri: direct:poll
                steps:
                  - pollEnrich: { uri: "seda:queue", timeout: 5000, aggregationStrategy: merge }
                  - to: mock:polled
            """);
        Mock("enriched").ExpectedBodiesReceived("x+resource");
        Mock("polled").ExpectedBodiesReceived("y+queued");

        await Template.SendBodyAsync("seda:queue", "queued");
        await Template.SendBodyAsync("direct:enrich", "x");
        await Template.SendBodyAsync("direct:poll", "y");

        await Mock("enriched").AssertIsSatisfiedAsync();
        await Mock("polled").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task ClaimCheck_pushes_and_pops()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - claimCheck: { operation: Push }
                  - setBody: { constant: temporary }
                  - to: mock:during
                  - claimCheck: { operation: Pop }
                  - to: mock:after
            """);
        Mock("during").ExpectedBodiesReceived("temporary");
        Mock("after").ExpectedBodiesReceived("original");

        await Template.SendBodyAsync("direct:start", "original");

        await Mock("during").AssertIsSatisfiedAsync();
        await Mock("after").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Pipeline_and_step_group_steps()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - step:
                      id: enrichment
                      steps:
                        - setHeader: { name: a, constant: "1" }
                  - pipeline:
                      steps:
                        - setHeader: { name: b, constant: "2" }
                        - to: mock:result
            """);
        Mock("result").ExpectedHeaderReceived("a", "1");
        Mock("result").ExpectedHeaderReceived("b", "2");

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    public sealed class Router
    {
        public string? Next(Exchange exchange)
        {
            var index = exchange.GetProperty("routed") as int? ?? 0;
            exchange.SetProperty("routed", index + 1);
            return index switch
            {
                0 => "mock:a",
                1 => "mock:b",
                _ => null,
            };
        }
    }
}
