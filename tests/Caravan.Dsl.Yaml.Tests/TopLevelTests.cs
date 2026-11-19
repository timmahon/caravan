using Caravan.Model;

namespace Caravan.Dsl.Yaml.Tests;

public class TopLevelTests : YamlTestBase
{
    [Fact]
    public async Task Route_options_are_applied()
    {
        await StartAsync("""
            - route:
                id: main
                description: The main route
                group: g1
                startupOrder: 5
                streamCache: false
                shutdownRoute: Defer
                properties:
                  - key: owner
                    value: team-a
                from:
                  id: input
                  uri: direct:main
                  steps: [ { to: mock:main } ]
            - route:
                id: lazy
                auto-startup: false
                from: { uri: direct:lazy }
            """);
        var main = Context.RouteDefinitions.Single(r => r.Id == "main");
        Assert.Equal("The main route", main.Description);
        Assert.Equal("g1", main.Group);
        Assert.Equal(5, main.StartupOrder);
        Assert.False(main.StreamCaching);
        Assert.Equal(ShutdownRoute.Defer, main.ShutdownRoute);
        Assert.Equal("team-a", main.Properties["owner"]);
        Assert.Equal("input", main.FromId);
        Assert.Equal(Spi.ServiceStatus.Started, Context.GetRouteStatus("main"));
        Assert.NotEqual(Spi.ServiceStatus.Started, Context.GetRouteStatus("lazy"));
        Mock("main").ExpectedMessageCount(1);
        await Template.SendBodyAsync("direct:main", "x");
        await Mock("main").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Route_template_and_templated_routes_with_parameters_and_beans()
    {
        await StartAsync("""
            - routeTemplate:
                id: greeter-template
                parameters:
                  - name: name
                  - name: greeting
                    defaultValue: Hello
                    description: The greeting
                  - name: times
                    defaultValue: "1"
                beans:
                  - name: myGreeter
                    type: "#class:Caravan.Dsl.Yaml.Tests.Greeter"
                    properties:
                      greeting: "{{greeting}}"
                      times: "{{times}}"
                from:
                  uri: "direct:{{name}}"
                  steps:
                    - bean: { ref: "{{myGreeter}}", method: Greet }
                    - setHeader: { name: source, constant: "{{name}}" }
                    - to: "mock:{{name}}"
            - templatedRoute:
                routeTemplateRef: greeter-template
                routeId: english
                parameters:
                  - name: name
                    value: en
            - templatedRoute:
                routeTemplateRef: greeter-template
                parameters:
                  - name: name
                    value: fr
                  - name: greeting
                    value: Bonjour
                  - name: times
                    value: 2
            """);
        Mock("en").ExpectedBodiesReceived("Hello Bob");
        Mock("en").ExpectedHeaderReceived("source", "en");
        Mock("fr").ExpectedBodiesReceived("Bonjour BobBonjour Bob");
        Mock("fr").ExpectedHeaderReceived("source", "fr");

        await Template.SendBodyAsync("direct:en", "Bob");
        await Template.SendBodyAsync("direct:fr", "Bob");

        await Mock("en").AssertIsSatisfiedAsync();
        await Mock("fr").AssertIsSatisfiedAsync();
        Assert.Contains(Context.RouteDefinitions, r => r.Id == "english" && r.TemplateId == "greeter-template");
        var template = Assert.Single(Context.RouteTemplateDefinitions);
        Assert.Equal("The greeting", template.Parameters[1].Description);
        Assert.False(template.Parameters[1].Required);
    }

    [Fact]
    public async Task Template_parameters_work_in_typed_options()
    {
        await StartAsync("""
            - routeTemplate:
                id: sampler
                parameters:
                  - name: every
                  - name: start
                    defaultValue: "true"
                route:
                  autoStartup: "{{start}}"
                  from:
                    uri: "direct:{{every}}"
                    steps:
                      - sample: { messageFrequency: "{{every}}" }
                      - to: "mock:every{{every}}"
            - templatedRoute:
                routeTemplateRef: sampler
                routeId: two
                parameters: [ { name: every, value: 2 } ]
            - templatedRoute:
                routeTemplateRef: sampler
                routeId: three
                parameters: [ { name: every, value: 3 } ]
            - templatedRoute:
                routeTemplateRef: sampler
                routeId: stopped
                parameters: [ { name: every, value: 4 }, { name: start, value: "false" } ]
            """);
        Mock("every2").ExpectedBodiesReceived(2, 4, 6);
        Mock("every3").ExpectedBodiesReceived(3, 6);

        for (var i = 1; i <= 6; i++)
        {
            await Template.SendBodyAsync("direct:2", i);
            await Template.SendBodyAsync("direct:3", i);
        }

        await Mock("every2").AssertIsSatisfiedAsync();
        await Mock("every3").AssertIsSatisfiedAsync();
        Assert.Equal("two", Context.RouteDefinitions.Single(r => r.FromUri == "direct:2").Id);
        Assert.False(Context.RouteDefinitions.Single(r => r.Id == "stopped").AutoStartup);
        Assert.Equal(0, Assert.IsType<SampleDefinition>(Assert.Single(Context.RouteTemplateDefinitions).Route!.Outputs[0]).MessageFrequency ?? 0);
    }

    [Fact]
    public async Task Template_with_route_wrapper_called_as_a_kamelet()
    {
        await StartAsync("""
            - routeTemplate:
                id: suffix
                parameters:
                  - name: value
                route:
                  from:
                    uri: kamelet:source
                    steps:
                      - setBody: { simple: "${body}{{value}}" }
            - from:
                uri: direct:start
                steps:
                  - kamelet: { name: "suffix?value=-1" }
                  - kamelet: suffix/second?value=-2
                  - to: mock:result
            """);
        Mock("result").ExpectedBodiesReceived("x-1-2");

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Route_configurations_in_camel_shape_are_selected_by_id()
    {
        await StartAsync("""
            - routeConfiguration:
                id: handled-config
                onException:
                  - onException:
                      handled: { constant: true }
                      exception: [ Caravan.Dsl.Yaml.Tests.TestBusinessException ]
                      steps:
                        - setBody: { simple: "handled: ${exception.message}" }
                        - to: mock:handled
                intercept:
                  - intercept:
                      steps: [ { to: mock:intercepted } ]
            - routeConfiguration:
                id: dlc-config
                errorHandler:
                  deadLetterChannel: mock:dead
                interceptSendToEndpoint:
                  - uri: "mock:target*"
                    skipSendToOriginalEndpoint: true
                    steps: [ { to: mock:redirected } ]
            - route:
                routeConfigurationId: handled-config
                from:
                  uri: direct:a
                  steps:
                    - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: oops }
            - route:
                routeConfigurationId: dlc-config
                from:
                  uri: direct:b
                  steps:
                    - to: mock:target1
                    - throwException: { exceptionType: InvalidOperationException, message: oops }
            """);
        Mock("handled").ExpectedBodiesReceived("handled: oops");
        Mock("intercepted").ExpectedMessageCount(1);
        Mock("redirected").ExpectedMessageCount(1);
        Mock("target1").ExpectedMessageCount(0);
        Mock("dead").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:b", "x");

        await Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Equal(2, Context.RouteConfigurationDefinitions.Count);
    }

    [Fact]
    public async Task Builder_level_interceptors_and_clauses_apply_to_the_file_routes()
    {
        await StartAsync("""
            - intercept:
                onWhen: { simple: "${body} == 'watch'" }
                steps: [ { to: mock:intercept } ]
            - interceptFrom:
                uri: "direct:a*"
                steps: [ { setHeader: { name: from, constant: intercepted } } ]
            - interceptSendToEndpoint:
                uri: mock:end
                afterUri: mock:after
            - onException:
                exception: [ Caravan.Dsl.Yaml.Tests.TestBusinessException ]
                continued: { constant: true }
            - onCompletion:
                steps: [ { to: mock:done } ]
            - from:
                uri: direct:a1
                steps:
                  - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: ignored }
                  - to: mock:end
            """);
        Mock("intercept").ExpectedMessageCount(2);
        Mock("end").ExpectedHeaderReceived("from", "intercepted");
        Mock("after").ExpectedMessageCount(1);
        Mock("done").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:a1", "watch");

        await Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Beans_are_created_with_properties_constructors_factory_and_init()
    {
        Context.PropertiesComponent.InitialProperties["city"] = "Paris";
        await StartAsync("""
            - beans:
                - name: plain
                  type: Caravan.Dsl.Yaml.Tests.Greeter
                  initMethod: Init
                  properties:
                    greeting: Hi
                    times: 2
                    address:
                      city: "{{city}}"
                      zip: 75001
                    other.city: Lyon
                    tags: [ a, b ]
                    friend: "#bean:built"
                - name: built
                  type: Caravan.Dsl.Yaml.Tests.Greeter, Caravan.Dsl.Yaml.Tests
                  constructors:
                    0: Yo
                    1: 3
                - name: listed
                  type: Caravan.Dsl.Yaml.Tests.Greeter
                  constructors: [ Hey, 1 ]
                - name: factory
                  type: Caravan.Dsl.Yaml.Tests.Greeter
                  factoryMethod: Create
                  constructors: [ Wow ]
            - from:
                uri: direct:start
                steps:
                  - bean: { ref: plain, method: Greet }
                  - to: mock:result
            """);
        Mock("result").ExpectedBodiesReceived("Hi BobHi Bob");

        await Template.SendBodyAsync("direct:start", "Bob");

        await Mock("result").AssertIsSatisfiedAsync();
        var plain = (Greeter)Context.Registry.Lookup("plain")!;
        Assert.True(plain.Initialized);
        Assert.Equal(("Paris", 75001), (plain.Address.City, plain.Address.Zip));
        Assert.Equal("Lyon", plain.Other!.City);
        Assert.Equal(["a", "b"], plain.Tags);
        Assert.Same(Context.Registry.Lookup("built"), plain.Friend);
        var built = (Greeter)Context.Registry.Lookup("built")!;
        Assert.Equal(("Yo", 3), (built.Greeting, built.Times));
        Assert.Equal("Hey", ((Greeter)Context.Registry.Lookup("listed")!).Greeting);
        Assert.Equal(("Wow!", 2), (((Greeter)Context.Registry.Lookup("factory")!).Greeting, ((Greeter)Context.Registry.Lookup("factory")!).Times));
    }

    [Fact]
    public async Task Several_documents_and_routes_without_wrapper()
    {
        await StartAsync("""
            - from:
                uri: direct:one
                steps: [ { to: mock:one } ]
            ---
            - from:
                uri: direct:two
                steps: [ { to: mock:two } ]
            """);
        Mock("one").ExpectedMessageCount(1);
        Mock("two").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:one", "x");
        await Template.SendBodyAsync("direct:two", "x");

        await Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Builder_is_reusable_and_FromStream_works()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("- from: { uri: direct:s, steps: [ { to: mock:s } ] }"));
        var builder = YamlRoutesBuilder.FromStream(stream, "stream.yaml");
        Assert.Equal("stream.yaml", builder.SourceName);
        await using var other = new CaravanContext();
        Assert.Single(builder.Build(other));
        Context.AddRoutes(builder);
        await Context.StartAsync();
        Mock("s").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:s", "x");

        await Mock("s").AssertIsSatisfiedAsync();
    }
}
