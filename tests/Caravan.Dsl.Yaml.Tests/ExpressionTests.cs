using Caravan.Language;
using Caravan.Model;

namespace Caravan.Dsl.Yaml.Tests;

public class ExpressionTests : YamlTestBase
{
    [Fact]
    public async Task Every_expression_syntax_evaluates()
    {
        Context.Registry.Bind("greeter", new Greeter { Greeting = "Hey" });
        Context.Registry.Bind("suffix", Expr.Constant("!"));
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - setProperty: { name: prop, constant: P }
                  - setHeader: { name: simple, simple: "${body}-s" }
                  - setHeader: { name: simpleLong, simple: { expression: "  ${body}  ", trim: false } }
                  - setHeader: { name: constant, constant: 42 }
                  - setHeader: { name: typed, constant: { expression: "42", resultType: int } }
                  - setHeader: { name: header, header: simple }
                  - setHeader: { name: headerLong, header: { expression: simple } }
                  - setHeader: { name: property, exchangeProperty: prop }
                  - setHeader: { name: method, method: { ref: greeter, method: Greet } }
                  - setHeader: { name: beanType, method: { beanType: Caravan.Dsl.Yaml.Tests.Greeter, method: Shout } }
                  - setHeader: { name: ref, ref: suffix }
                  - setHeader: { name: wrapped, expression: { simple: "${header.constant}" } }
                  - setHeader: { name: generic, language: { language: simple, expression: "${body}g" } }
                  - setHeader: { name: tokenized, tokenize: "," }
                  - to: mock:result
            """);
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "a,b");

        await mock.AssertIsSatisfiedAsync();
        var message = mock.ReceivedExchanges[0].Message;
        Assert.Equal("a,b-s", message.GetHeader("simple"));
        Assert.Equal("  a,b  ", message.GetHeader("simpleLong"));
        Assert.Equal("42", message.GetHeader("constant"));
        Assert.Equal(42, message.GetHeader("typed"));
        Assert.Equal("a,b-s", message.GetHeader("header"));
        Assert.Equal("a,b-s", message.GetHeader("headerLong"));
        Assert.Equal("P", message.GetHeader("property"));
        Assert.Equal("Hey a,b", message.GetHeader("method"));
        Assert.Equal("A,B!", message.GetHeader("beanType"));
        Assert.Equal("!", message.GetHeader("ref"));
        Assert.Equal("42", message.GetHeader("wrapped"));
        Assert.Equal("a,bg", message.GetHeader("generic"));
        Assert.Equal(["a", "b"], ((IEnumerable<object>)message.GetHeader("tokenized")!).Cast<string>());
    }

    [Fact]
    public async Task Predicates_in_every_syntax()
    {
        Context.Registry.Bind("greeter", new Greeter());
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - choice:
                      when:
                        - method: { ref: greeter, method: IsVip }
                          steps: [ { to: mock:vip } ]
                        - expression: { header: urgent }
                          steps: [ { to: mock:urgent } ]
                        - language: { language: simple, expression: "${body} == 'x'" }
                          steps: [ { to: mock:x } ]
                      otherwise:
                        steps: [ { to: mock:other } ]
            """);
        Mock("vip").ExpectedBodiesReceived("vip1");
        Mock("urgent").ExpectedBodiesReceived("u");
        Mock("x").ExpectedBodiesReceived("x");
        Mock("other").ExpectedBodiesReceived("y");

        await Template.SendBodyAsync("direct:start", "vip1");
        await Template.SendBodyAndHeaderAsync("direct:start", "u", "urgent", true);
        await Template.SendBodyAsync("direct:start", "x");
        await Template.SendBodyAsync("direct:start", "y");

        await Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Languages_registered_in_the_context_are_accepted()
    {
        Context.Languages.Add("reverse", new ReverseLanguage());
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - filter:
                      reverse: "yes"
                      steps:
                        - setBody: { reverse: "?" }
                        - to: mock:result
            """);
        Mock("result").ExpectedBodiesReceived("cba?");

        await Template.SendBodyAsync("direct:start", "abc");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Property_placeholders_are_resolved_in_non_simple_expressions_and_typed_options()
    {
        Context.PropertiesComponent.InitialProperties["greeting"] = "hello";
        Context.PropertiesComponent.InitialProperties["copies"] = "2";
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - setBody: { constant: "{{greeting}} world" }
                  - loop:
                      constant: "{{copies}}"
                      steps: [ { to: mock:result } ]
                  - split:
                      tokenize: " "
                      maxDegreeOfParallelism: "{{copies}}"
                      steps: [ { to: mock:word } ]
            """);
        Mock("result").ExpectedBodiesReceived("hello world", "hello world");
        Mock("word").ExpectedBodiesReceived("hello", "world");

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        await Mock("word").AssertIsSatisfiedAsync();
        Assert.Equal(2, Assert.IsType<SplitDefinition>(Context.RouteDefinitions[0].Outputs[2]).MaxDegreeOfParallelism);
    }

    [Fact]
    public void Non_simple_languages_become_language_expressions_and_simple_stays_simple()
    {
        Context.AddRoutes(YamlRoutesBuilder.FromString("""
            - from:
                uri: direct:start
                steps:
                  - setBody: { constant: x }
                  - setHeader: { name: s, simple: "${body}" }
            """));
        var outputs = Context.RouteDefinitions[0].Outputs;
        var constant = Assert.IsType<LanguageExpression>(Assert.IsType<SetBodyDefinition>(outputs[0]).Expression);
        Assert.Equal(("constant", "x"), (constant.Language, constant.Text));
        Assert.IsType<SimpleBuilder>(Assert.IsType<SetHeaderDefinition>(outputs[1]).Expression);
    }

    [Fact]
    public async Task Boolean_scalars_are_accepted_as_constant_predicates()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - onException:
                      exception: [ Exception ]
                      handled: true
                      steps: [ { to: mock:handled } ]
                  - throwException: { exceptionType: InvalidOperationException, message: x }
            """);
        Mock("handled").ExpectedMessageCount(1);

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        await Mock("handled").AssertIsSatisfiedAsync();
        Assert.Null(exchange.Exception);
    }
}
