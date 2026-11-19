namespace Caravan.Dsl.Yaml.Tests;

public class ErrorMessageTests : YamlTestBase
{
    [Fact]
    public void Unknown_step_has_line_column_and_suggestion()
    {
        var error = LoadError("""
            - from:
                uri: direct:start
                steps:
                  - to: mock:a
                  - setBdy: { constant: x }
            """);
        Assert.Equal((5, 9), (error.Line, error.Column));
        Assert.Equal("test.yaml", error.SourceName);
        Assert.Equal("test.yaml:5:9: Unknown step 'setBdy'. Did you mean 'setBody'?", error.Message);
    }

    [Fact]
    public void Unknown_option_lists_valid_options()
    {
        var error = LoadError("""
            - from:
                uri: direct:start
                steps:
                  - split:
                      tokenize: ","
                      parallelProcesing: true
            """);
        Assert.Equal((6, 11), (error.Line, error.Column));
        Assert.Contains("Unknown option 'parallelProcesing' in 'split'. Did you mean 'parallelProcessing'?", error.Reason);
        Assert.Contains("Valid options: delimiter, aggregationStrategy", error.Reason);
    }

    [Fact]
    public void Wrong_value_type_is_reported_at_the_value()
    {
        var error = LoadError("""
            - from:
                uri: direct:start
                steps:
                  - aggregate:
                      correlationExpression: { header: id }
                      aggregationStrategy: s
                      completionSize: lots
            """);
        Assert.Equal((7, 27), (error.Line, error.Column));
        Assert.Equal("Option 'completionSize' of 'aggregate' must be an integer but was 'lots'", error.Reason);
    }

    [Theory]
    [InlineData("- from: { uri: direct:a, steps: [ { setHeader: { constant: x } } ] }", "Missing required option 'name' in 'setHeader'")]
    [InlineData("- from: { uri: direct:a, steps: [ { setHeader: { name: x } } ] }", "'setHeader' needs an expression")]
    [InlineData("- from: { steps: [] }", "Missing required option 'uri' in 'from'")]
    [InlineData("- route: { id: x }", "Missing required option 'from' in 'route'")]
    [InlineData("- from: { uri: direct:a, steps: [ { to: { uri: mock:a, pattern: Sideways } } ] }", "Option 'pattern' of 'to' must be one of InOnly, InOut but was 'Sideways'")]
    [InlineData("- from: { uri: direct:a, steps: [ { filter: { simple: '${body', steps: [] } } ] }", "Invalid simple expression")]
    [InlineData("- from: { uri: direct:a, steps: [ { setBody: { groovy: x } } ] }", "Unknown option 'groovy' in 'setBody'")]
    [InlineData("- from: { uri: direct:a, steps: [ { convertBodyTo: No.Such.Type } ] }", "Cannot resolve type 'No.Such.Type'")]
    [InlineData("- from: { uri: direct:a, steps: [ { threads: { poolSize: 2 } } ] }", "Step 'threads' is not supported by Caravan")]
    [InlineData("- from: { uri: direct:a, steps: [ { toD: { uri: x, variableSend: v } } ] }", "Option 'variableSend' of 'toD' is not supported by Caravan: variables are not supported")]
    [InlineData("- from: { uri: direct:a, steps: [ { to: x, log: y } ] }", "A step must be an object with exactly one key")]
    [InlineData("- from: { uri: direct:a, steps: { to: x } }", "'steps' must be a list of steps")]
    [InlineData("- camelContext: { }", "'camelContext' is not supported by Caravan")]
    [InlineData("- setBody: { constant: x }", "Unknown top-level element 'setBody' ('setBody' is a step: put it in the steps of a route)")]
    [InlineData("route: { from: { uri: direct:a } }", "A YAML DSL document must be a list of elements")]
    [InlineData("- from: { uri: direct:a, steps: [ { aggregate: { correlationExpression: x, aggregationStrategy: s } } ] }", "'aggregate.correlationExpression' must be an object with one expression")]
    [InlineData("- from: { uri: direct:a, steps: [ { onCompletion: {} }, { filter: { simple: x, steps: [ { onException: { exception: [ Exception ] } } ] } } ] }", "'onException' is only allowed among the top-level steps of a route")]
    [InlineData("- from: { uri: direct:a, steps: [ { marshal: { json: {}, csv: {} } } ] }", "'marshal' needs exactly one data format")]
    [InlineData("- from: { uri: direct:a, steps: [ { marshal: { json: { prettyPrnt: true } } } ] }", "Unknown option 'prettyPrnt' of data format 'json'. Did you mean 'prettyPrint'?")]
    [InlineData("- from: { uri: direct:a, steps: [ { loadBalance: { steps: [] } } ] }", "loadBalance needs exactly one load balancer")]
    [InlineData("- from: { uri: direct:a, steps: [ { doTry: { steps: [] } } ] }", "doTry needs at least one doCatch or a doFinally")]
    [InlineData("- from: { uri: direct:a, steps: [ { setBody: { simple: a, constant: b } } ] }", "'setBody' has more than one expression")]
    [InlineData("- from: { uri: direct:a, steps: [ { setBody: { foo: a } } ] }", "Unknown option 'foo' in 'setBody'. Valid options: id, description, disabled (or an expression language such as simple, constant, header)")]
    [InlineData("- from: { uri: direct:a, steps: [ { setBody: { expression: { nope: a } } } ] }", "Unknown expression language 'nope'")]
    [InlineData("- beans: [ { name: b, type: No.Such.Bean } ]", "Cannot resolve the type 'No.Such.Bean' of bean 'b'")]
    [InlineData("- beans: [ { name: b, type: Caravan.Dsl.Yaml.Tests.Greeter, properties: { nope: 1 } } ]", "has no writable property 'nope'")]
    [InlineData("- templatedRoute: { routeTemplateRef: missing }", "No route template exists with id missing")]
    public void Invalid_definitions_have_precise_messages(string yaml, string expected)
    {
        var error = Record.Exception(() => Context.AddRoutes(YamlRoutesBuilder.FromString(yaml, "test.yaml")));
        Assert.NotNull(error);
        Assert.Contains(expected, error.Message);
        if (error is YamlRouteDefinitionException located)
        {
            Assert.True(located.Line > 0 && located.Column > 0, located.Message);
        }
    }

    [Fact]
    public void Yaml_syntax_errors_have_line_and_column()
    {
        var error = Assert.Throws<YamlRouteDefinitionException>(() => YamlRoutesBuilder.FromString("""
            - from:
                uri: direct:a
                steps: [ { to: mock:a }
            """, "broken.yaml"));
        Assert.StartsWith("broken.yaml:", error.Message);
        Assert.Contains("Invalid YAML", error.Reason);
        Assert.True(error.Line >= 3);
    }

    [Fact]
    public void Typed_options_with_bad_template_parameter_values_fail_when_the_route_is_created()
    {
        var error = Record.Exception(() => Context.AddRoutes(YamlRoutesBuilder.FromString("""
            - routeTemplate:
                id: t
                parameters: [ { name: every } ]
                from:
                  uri: direct:t
                  steps:
                    - sample: { messageFrequency: "{{every}}" }
            - templatedRoute:
                routeTemplateRef: t
                parameters: [ { name: every, value: often } ]
            """, "test.yaml")));
        Assert.NotNull(error);
        var located = Assert.IsType<YamlRouteDefinitionException>(error is YamlRouteDefinitionException ? error : error.InnerException);
        Assert.Equal("Option 'messageFrequency' of 'sample' must be an integer but was 'often'", located.Reason);
        Assert.Equal(7, located.Line);
    }

    [Fact]
    public void File_errors_name_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"caravan-yaml-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "- from:\n    uri: direct:a\n    stepz: []\n");
        try
        {
            var error = Assert.Throws<YamlRouteDefinitionException>(() => Context.AddRoutes(YamlRoutesBuilder.FromFile(path)));
            Assert.Equal($"{path}:3:5: Unknown option 'stepz' in 'from'. Did you mean 'steps'? Valid options: uri, parameters, id, description, steps", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
