using Caravan.Builder;
using Caravan.Components.Bean;
using Caravan.Language;

namespace Caravan.Core.Tests.Beans;

public class BeanComponentTests : ContextTestBase
{
    [Fact]
    public async Task Bean_endpoint_calls_the_registry_bean()
    {
        Context.Registry.Bind("greeter", new Greeter());
        Context.Registry.Bind("lang", new GreeterWithLanguage());
        await StartWithRoutes(b =>
        {
            b.From("direct:in").To("bean:greeter?method=Greet").To("mock:out");
            b.From("direct:params").To("bean:lang?method=Greet(${body}, 'fr')");
        });
        var mock = Mock("mock:out").ExpectedBodiesReceived("Hello one");

        await Template.SendBodyAsync("direct:in", "one");

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal("Bonjour Zoe", await Template.RequestBodyAsync("direct:params", "Zoe"));
        Assert.Equal("Greet", Context.GetEndpoint<BeanEndpoint>("bean:greeter?method=Greet").Method);
    }

    [Fact]
    public async Task Bean_options_configure_the_bean()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:in").To($"class:{typeof(Greeter).FullName}?bean.prefix=Hey");
            b.From("direct:prototype").To($"class:{typeof(InstanceIdBean).FullName}?method=WhoAmI&scope=Prototype");
        });

        Assert.Equal("Hey you", await Template.RequestBodyAsync("direct:in", "you"));
        Assert.NotEqual(await Template.RequestBodyAsync("direct:prototype", null), await Template.RequestBodyAsync("direct:prototype", null));
        Assert.Equal(BeanScope.Prototype, Context.GetEndpoint<BeanEndpoint>($"class:{typeof(InstanceIdBean).FullName}?method=WhoAmI&scope=Prototype").Scope);
    }

    [Fact]
    public async Task Class_endpoint_calls_static_methods_of_static_classes()
    {
        await StartWithRoutes(b => b.From("direct:in").To($"class:{typeof(StaticBean).FullName}?method=Twice"));

        Assert.Equal("xx", await Template.RequestBodyAsync("direct:in", "x"));
    }

    [Fact]
    public async Task Bad_bean_uris_fail()
    {
        Context.Registry.Bind("greeter", new Greeter());
        await Context.StartAsync();

        Assert.Throws<NoSuchBeanException>(() => Context.GetEndpoint("bean:missing"));
        Assert.Throws<MethodNotFoundException>(() => Context.GetEndpoint("bean:greeter?method=nope"));
        Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("class:No.Such.Type"));
        Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("bean:greeter?unknown=1"));
        Assert.Throws<NotSupportedException>(() => Context.GetEndpoint("bean:greeter").CreateConsumer(new Spi.DelegateProcessor(_ => ValueTask.CompletedTask)));
    }

    [Fact]
    public async Task Simple_bean_function_calls_bean_methods()
    {
        Context.Registry.Bind("greeter", new Greeter());
        Context.Registry.Bind("lang", new GreeterWithLanguage());
        Context.Registry.Bind("check", new PredicateBean());
        await StartWithRoutes(b =>
        {
            b.From("direct:query").SetBody(Expr.Simple("${bean:greeter?method=Greet}"));
            b.From("direct:dotted").SetBody(Expr.Simple("${bean:greeter.Greet} and more"));
            b.From("direct:nested").SetBody(Expr.Simple("${bean:lang?method=Greet(${body}, ${header.lang})}"));
            b.From("direct:class").SetBody(Expr.Simple($"${{bean:class:{typeof(StaticBean).FullName}?method=Twice}}"));
            b.From("direct:filter").Filter(Pred.Simple("${bean:check?method=IsBig}"), f => f.To("mock:big"));
        });
        Mock("mock:big").ExpectedBodiesReceived(500);

        Assert.Equal("Hello a", await Template.RequestBodyAsync("direct:query", "a"));
        Assert.Equal("Hello b and more", await Template.RequestBodyAsync("direct:dotted", "b"));
        Assert.Equal("Bonjour c", await Template.RequestBodyAndHeaderAsync("direct:nested", "c", "lang", "fr"));
        Assert.Equal("dd", await Template.RequestBodyAsync("direct:class", "d"));
        await Template.SendBodyAsync("direct:filter", 5);
        await Template.SendBodyAsync("direct:filter", 500);
        await Mock("mock:big").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Method_expressions_and_the_bean_language()
    {
        Context.Registry.Bind("greeter", new Greeter());
        Context.Registry.Bind("check", new PredicateBean());
        await StartWithRoutes(b =>
        {
            b.From("direct:instance").SetHeader("greeting", Expr.Method(new Greeter { Prefix = "Yo" }, "Greet")).SetBody(Expr.Header("greeting"));
            b.From("direct:name").Transform(Expr.Method("greeter"));
            b.From("direct:type").Transform(Expr.Method(typeof(StaticBean), "Twice"));
            b.From("direct:language").Transform(Lang.Language("method", "greeter?method=Greet"));
            b.From("direct:void").SetHeader("h", Expr.Method(new VoidBean(), "Touch")).To("mock:void");
        });
        Mock("mock:void").ExpectedBodiesReceived("kept");

        Assert.Equal("Yo x", await Template.RequestBodyAsync("direct:instance", "x"));
        Assert.Equal("Hello y", await Template.RequestBodyAsync("direct:name", "y"));
        Assert.Equal("zz", await Template.RequestBodyAsync("direct:type", "z"));
        Assert.Equal("Hello w", await Template.RequestBodyAsync("direct:language", "w"));
        await Template.SendBodyAsync("direct:void", "kept");
        await Mock("mock:void").AssertIsSatisfiedAsync();
        Assert.True(Mock("mock:void").ReceivedExchanges[0].Message.GetHeader<bool>("touched"));

        var exchange = new Exchange(Context);
        exchange.Message.Body = 150;
        Assert.True(Context.ResolveLanguage("bean").CreatePredicate("check?method=IsBig").Matches(exchange));
        Assert.IsType<BeanLanguage>(Context.ResolveLanguage("method"));
        Assert.Throws<ArgumentException>(() => BeanExpression.Parse("greeter?method=a&unknown=1"));
        Assert.Throws<ArgumentException>(() => BeanExpression.Parse("class:No.Such.Type"));
    }

    [Fact]
    public async Task Bean_language_scope_and_bean_options()
    {
        Context.Registry.Bind("greeter", new Greeter());
        await StartWithRoutes(b => b.From("direct:in").Transform(Expr.Simple($"${{bean:class:{typeof(Greeter).FullName}?method=Greet&bean.prefix=Howdy&scope=Request}}")));

        Assert.Equal("Howdy x", await Template.RequestBodyAsync("direct:in", "x"));
    }
}
