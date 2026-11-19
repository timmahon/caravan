using Caravan.Builder;
using Caravan.Language;
using Caravan.Model;

namespace Caravan.Core.Tests.Languages;

public class LanguageExpressionTests : ContextTestBase
{
    [Fact]
    public void Resolves_the_language_on_first_use_and_converts_the_result()
    {
        var exchange = new Exchange(Context);
        exchange.Message.SetHeader("n", "42");

        Assert.Equal(42, new LanguageExpression("header", "n", typeof(int)).Evaluate(exchange));
        Assert.True(new LanguageExpression("constant", "true").Matches(exchange));
        Assert.Equal("header{n}", new LanguageExpression("header", "n").ToString());
        Assert.Throws<NoSuchLanguageException>(() => new LanguageExpression("nope", "x").Evaluate(exchange));
    }

    [Fact]
    public void Resolves_property_placeholders_except_for_simple()
    {
        Context.PropertiesComponent.InitialProperties["greeting"] = "hi";
        var exchange = new Exchange(Context);

        Assert.Equal("hi there", new LanguageExpression("constant", "{{greeting}} there").Evaluate(exchange));
        Assert.Equal("hi", new LanguageExpression("simple", "{{greeting}}").Evaluate(exchange));
    }

    [Fact]
    public async Task Route_templates_substitute_parameters_into_its_text()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.RouteTemplate("t")
            .TemplateParameter("text")
            .From("direct:{{text}}")
            .SetBody(Lang.Language("constant", "{{text}}!"))
            .To("mock:result")));
        await Context.AddRouteFromTemplateAsync("r1", "t", new Dictionary<string, object?> { ["text"] = "a" });
        await Context.AddRouteFromTemplateAsync("r2", "t", new Dictionary<string, object?> { ["text"] = "b" });
        await Context.StartAsync();
        Mock("result").ExpectedBodiesReceived("a!", "b!");

        await Template.SendBodyAsync("direct:a", "x");
        await Template.SendBodyAsync("direct:b", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        var copy = Assert.IsType<LanguageExpression>(Assert.IsType<SetBodyDefinition>(Context.RouteDefinitions.Single(r => r.Id == "r1").Outputs[0]).Expression);
        Assert.Equal("a!", copy.Text);
    }

    [Fact]
    public void RemoveRouteConfiguration_forgets_a_configuration()
    {
        var configuration = new RouteConfigurationDefinition { Id = "c" };
        Context.AddRouteConfiguration(configuration);

        Assert.True(Context.RemoveRouteConfiguration(configuration));
        Assert.False(Context.RemoveRouteConfiguration(configuration));
        Assert.Empty(Context.RouteConfigurationDefinitions);
    }
}
