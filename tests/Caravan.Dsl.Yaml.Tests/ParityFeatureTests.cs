using Caravan.DataTypes;
using Caravan.Model;
using Caravan.RoutePolicies;
using Caravan.Spi;

namespace Caravan.Dsl.Yaml.Tests;

/// <summary>
/// YAML for the features of the parity audit: variables, threads, poll, route policies, preconditions, node prefixes, data type contracts,
/// transformers and validators, Kamelet consumers.
/// </summary>
public class ParityFeatureTests : YamlTestBase
{
    [Fact]
    public async Task Variable_steps_and_variable_send_receive()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                variableReceive: input
                steps:
                  - setVariable: { name: a, simple: "${variable.input}" }
                  - setVariables:
                      variables:
                        - { name: b, constant: "2" }
                        - { name: global:seen, constant: "yes" }
                  - convertVariableTo: { name: b, type: int }
                  - to: { uri: direct:service, variableSend: a, variableReceive: reply }
                  - toD: { uri: "direct:${variable.a}", variableReceive: d }
                  - enrich: { uri: direct:service, variableSend: b, variableReceive: e }
                  - wireTap: { uri: mock:tap, variableSend: a }
                  - removeVariable: input*
                  - setBody: { simple: "${variable.reply}|${variable.d}|${variable.e}|${variable.b}|${variableAs(b,int)}" }
                  - setHeader: { name: lang, variable: global:seen }
                  - to: mock:result
            - from:
                uri: direct:service
                steps:
                  - setBody: { simple: "s(${body})" }
            - from:
                uri: direct:service2
                steps:
                  - setBody: { constant: unused }
            """);

        Mock("tap").ExpectedMessageCount(1);
        await Template.SendBodyAsync("direct:start", "service2");

        var exchange = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.Equal("s(service2)|unused|s(2)|2|2", exchange.Message.Body);
        Assert.Equal("yes", exchange.Message.GetHeader("lang"));
        Assert.Null(exchange.GetVariable("input"));
        Assert.Equal("yes", Context.GetVariable("seen"));
        await Mock("tap").AssertIsSatisfiedAsync();
        Assert.Equal(["service2"], Mock("tap").ReceivedBodies);
    }

    [Fact]
    public async Task Poll_and_poll_enrich_with_variable_receive()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - pollEnrich: { uri: seda:a, timeout: 5s, variableReceive: p }
                  - poll: { uri: "seda:${header.q}", timeout: 5000 }
                  - setHeader: { name: p, variable: p }
                  - to: mock:result
            """);
        await Template.SendBodyAsync("seda:a", "first");
        await Template.SendBodyAsync("seda:b", "second");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "q", "b");

        var exchange = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.Equal("second", exchange.Message.Body);
        Assert.Equal("first", exchange.Message.GetHeader("p"));
    }

    [Fact]
    public async Task Threads_takes_the_rest_of_the_route_or_its_steps()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - threads: { poolSize: 2, maxPoolSize: 4, maxQueueSize: 10, rejectedPolicy: Abort, threadName: pool }
                  - setBody: { simple: "t ${body}" }
                  - to: mock:result
            - from:
                uri: direct:block
                steps:
                  - threads:
                      callerRunsWhenRejected: false
                      steps:
                        - to: mock:inside
                  - to: mock:after
            """);

        Assert.Equal("t x", await Template.RequestBodyAsync("direct:start", "x"));
        await Template.SendBodyAsync("direct:block", "y");

        var threads = Assert.IsType<ThreadsDefinition>(Context.RouteDefinitions[0].Outputs[0]);
        Assert.Equal((2, 4, 10, ThreadPoolRejectedPolicy.Abort, "pool"), (threads.PoolSize, threads.MaxPoolSize, threads.MaxQueueSize, threads.RejectedPolicy, threads.ThreadName));
        var block = Assert.IsType<ThreadsDefinition>(Context.RouteDefinitions[1].Outputs[0]);
        Assert.Equal(ThreadPoolRejectedPolicy.Abort, block.RejectedPolicy);
        Assert.Single(Mock("inside").ReceivedExchanges);
        Assert.Single(Mock("after").ReceivedExchanges);
    }

    [Fact]
    public async Task Route_options_precondition_node_prefix_and_route_policy()
    {
        var policy = new ThrottlingInflightRoutePolicy();
        Context.Registry.Bind("throttle", policy);
        Context.PropertiesComponent.InitialProperties["enabled"] = "false";
        await StartAsync("""
            - route:
                id: kept
                nodePrefixId: "k-"
                routePolicy: throttle
                from:
                  uri: direct:kept
                  steps:
                    - to: { id: send, uri: mock:kept }
            - route:
                id: dropped
                precondition: "{{enabled}}"
                from: { uri: direct:dropped, steps: [ { to: mock:dropped } ] }
            - routeConfiguration:
                id: cfg
                precondition: "false"
            """);

        var route = Assert.Single(Context.RouteDefinitions);
        Assert.Equal("kept", route.Id);
        Assert.Equal("k-send", route.Outputs[0].Id);
        Assert.Equal("throttle", route.RoutePolicyRefs);
        Assert.Same(policy, Assert.Single(((Impl.Route)Context.GetRoute("kept")!).RoutePolicies));
        Assert.Empty(Context.RouteConfigurationDefinitions);
    }

    [Fact]
    public async Task Templated_route_prefix_id_and_kamelet_consumer()
    {
        await StartAsync("""
            - routeTemplate:
                id: queueSource
                parameters: [ { name: queue } ]
                from:
                  uri: "seda:{{queue}}"
                  steps:
                    - setBody: { simple: "k-${body}" }
                    - to: { id: sink, uri: "kamelet:sink" }
            - templatedRoute:
                routeTemplateRef: queueSource
                routeId: plain
                prefixId: "p-"
                parameters: [ { name: queue, value: other } ]
            - from:
                uri: "kamelet:queueSource?queue=q"
                steps:
                  - to: mock:result
            """);
        Mock("result").ExpectedBodiesReceived("k-1");

        await Template.SendBodyAsync("seda:q", "1");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal("p-sink", Context.RouteDefinitions.Single(r => r.Id == "plain").Outputs[1].Id);
    }

    [Fact]
    public async Task Data_type_contracts_transformers_and_validators()
    {
        Context.Registry.Bind("upper", new DelegateTransformer(DataType.Parse("text"), DataType.Parse("upper"), m => m.Body = ((string)m.Body!).ToUpperInvariant()));
        await StartAsync("""
            - transformers:
                - dataFormatTransformer:
                    fromType: "clr:Caravan.Dsl.Yaml.Tests.TestUser"
                    toType: json
                    json: {}
                - endpointTransformer: { fromType: csv, toType: "xml:Row", uri: direct:csvToXml }
                - customTransformer: { ref: upper, toType: upper }
            - validators:
                - predicateValidator: { type: "xml:Row", simple: "${body} startsWith '<row>'" }
                - endpointValidator: { type: upper, uri: direct:checkUpper }
            - from:
                uri: direct:csvToXml
                steps:
                  - setBody: { simple: "<row>${body}</row>" }
            - from:
                uri: direct:checkUpper
                steps:
                  - filter: { simple: "${body} == 'BAD'", steps: [ { throwException: { exceptionType: System.InvalidOperationException, message: bad } } ] }
            - route:
                inputType: { urn: "xml:Row", validate: true }
                from: { uri: direct:rows, steps: [ { to: mock:rows } ] }
            - from:
                uri: direct:users
                steps:
                  - transform: { toType: json, fromType: "clr:Caravan.Dsl.Yaml.Tests.TestUser" }
                  - convertBodyTo: string
                  - to: mock:users
            - route:
                inputType: text
                outputType: { urn: upper, validate: true }
                from: { uri: direct:upper, steps: [ { to: mock:upper } ] }
            """);

        await Template.SendAsync("direct:rows", e =>
        {
            e.Message.Body = "a,b";
            e.Message.DataType = DataType.Parse("csv");
        });
        await Template.SendBodyAsync("direct:users", new TestUser { Name = "Ann", Id = 3 });
        var upper = await Template.RequestBodyAsync("direct:upper", "abc");
        var invalid = await Template.SendAsync("direct:upper", e => e.Message.Body = "bad");

        Assert.Equal(["<row>a,b</row>"], Mock("rows").ReceivedBodies);
        Assert.Contains("\"name\":\"Ann\"", (string)Mock("users").ReceivedBodies[0]!);
        Assert.Equal("ABC", upper);
        Assert.IsType<ValidationException>(invalid.Exception);
        Assert.Equal(3, Context.Transformers.All.Count);
        Assert.Equal(2, Context.Validators.All.Count);
    }
}
