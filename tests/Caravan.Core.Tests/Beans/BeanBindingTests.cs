using Caravan.Builder;
using Caravan.Spi;

namespace Caravan.Core.Tests.Beans;

public class BeanBindingTests : ContextTestBase
{
    [Fact]
    public async Task The_single_public_method_is_called_and_its_result_becomes_the_body()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new Greeter()).To("mock:out"));
        var mock = Mock("mock:out").ExpectedBodiesReceived("Hello World");

        await Template.SendBodyAsync("direct:in", "World");

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Handler_methods_are_preferred()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new HandlerBean()));

        Assert.Equal("handled x", await Template.RequestBodyAsync("direct:in", "x"));
    }

    [Theory]
    [InlineData("text", "string text")]
    [InlineData(5, "int 5")]
    public async Task Overloads_are_chosen_by_the_body_type(object body, string expected)
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new OverloadBean(), "Describe"));

        Assert.Equal(expected, await Template.RequestBodyAsync("direct:in", body));
        Assert.Equal("order o1", await Template.RequestBodyAsync("direct:in", new Order("o1", 1)));
    }

    [Fact]
    public async Task A_body_convertible_to_several_overloads_is_ambiguous()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new OverloadBean(), "Describe"));

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:in", 5L));

        var ambiguous = Assert.IsType<AmbiguousMethodCallException>(e.InnerException);
        Assert.Equal(2, ambiguous.Methods.Count);
    }

    [Fact]
    public async Task Without_a_method_name_several_candidates_are_ambiguous()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new AmbiguousBean()));

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:in", "x"));

        Assert.IsType<AmbiguousMethodCallException>(e.InnerException);
    }

    [Fact]
    public async Task Methods_without_body_parameters_are_chosen_when_nothing_takes_the_body()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:ping").Bean(new NoBodyBean());
            b.From("direct:twice").Bean(new NoBodyBean());
        });

        Assert.Equal("pong", await Template.RequestBodyAsync("direct:ping", "not a number"));
        Assert.Equal("8", await Template.RequestBodyAsync("direct:twice", 4));
        Assert.Equal("8", await Template.RequestBodyAsync("direct:twice", "4"));
    }

    [Fact]
    public async Task Method_names_match_ignoring_case_as_a_fallback()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new Greeter(), "greet"));

        Assert.Equal("Hello you", await Template.RequestBodyAsync("direct:in", "you"));
    }

    [Fact]
    public async Task An_unknown_method_fails_route_creation()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:in").Bean(new Greeter(), "nope")));

        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());

        Assert.Equal("nope", Assert.IsType<MethodNotFoundException>(e.InnerException).MethodName);
    }

    [Theory]
    [InlineData("Describe(string, int)", "string,int 7 0")]
    [InlineData("Describe(int, string)", "int,string 7 ")]
    [InlineData("Describe(System.Int32, *)", "int,string 7 ")]
    [InlineData("Describe(int)", "int 7")]
    public async Task Parameter_types_choose_the_overload(string method, string expected)
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new OverloadBean(), method));

        Assert.Equal(expected, await Template.RequestBodyAsync("direct:in", "7"));
    }

    [Fact]
    public async Task Parameter_types_matching_nothing_fail()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:in").Bean(new OverloadBean(), "Describe(bool)")));

        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());

        Assert.IsType<MethodNotFoundException>(e.InnerException);
    }

    [Fact]
    public async Task Parameter_values_are_simple_expressions_and_literals()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:greet").Bean(new GreeterWithLanguage(), "Greet(${body}, ${header.lang})");
            b.From("direct:shout").Bean(new GreeterWithLanguage(), "Shout('ab, c', 2, true)");
            b.From("direct:null").Bean(new GreeterWithLanguage(), "Nullable(null)");
            b.From("direct:mixed").Bean(new GreeterWithLanguage(), "Shout(${body}, ${header.n}, false)");
        });

        Assert.Equal("Bonjour Ann", await Template.RequestBodyAndHeaderAsync("direct:greet", "Ann", "lang", "fr"));
        Assert.Equal("AB, CAB, C!", await Template.RequestBodyAsync("direct:shout", null));
        Assert.Equal("was null", await Template.RequestBodyAsync("direct:null", "ignored"));
        Assert.Equal("XXX", await Template.RequestBodyAndHeaderAsync("direct:mixed", "x", "n", "3"));
    }

    [Fact]
    public async Task Binding_attributes_bind_body_headers_properties_and_simple()
    {
        await StartWithRoutes(b => b.From("direct:in").SetProperty("prop", Expr.Constant("p1")).Bean(new AttributeBean(), "All"));

        var result = await Template.RequestBodyAndHeadersAsync("direct:in", "body", new Dictionary<string, object?> { ["count"] = "3", ["other"] = 1 });

        Assert.Equal("body|3|2|p1|True|3 items", result);
    }

    [Fact]
    public async Task Exchange_exception_binds_the_caught_exception_by_type()
    {
        await StartWithRoutes(b => b.From("direct:in")
            .DoTry(
                t => t.Process(_ => throw new InvalidOperationException("broken")),
                c => c.DoCatch<InvalidOperationException>(r => r.Bean(new AttributeBean(), "Error"))));

        Assert.Equal("broken|none", await Template.RequestBodyAsync("direct:in", "x"));
    }

    [Fact]
    public async Task Exchange_message_context_token_and_converter_are_bound_by_type()
    {
        Context.Name = "ctx";
        await StartWithRoutes(b => b.From("direct:in").Bean(new AutoBoundBean()).To("mock:out"));
        var mock = Mock("mock:out").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:in", "b");

        await mock.AssertIsSatisfiedAsync();
        var message = mock.ReceivedExchanges[0].Message;
        Assert.Equal("b|True|ctx|True|True|True", message.Body);
        Assert.NotNull(message.GetHeader("seen"));
    }

    [Fact]
    public async Task Async_methods_are_awaited()
    {
        var bean = new AsyncBean();
        await StartWithRoutes(b =>
        {
            b.From("direct:upper").Bean(bean, "UpperAsync");
            b.From("direct:record").Bean(bean, "RecordAsync");
            b.From("direct:length").Bean(bean, "LengthAsync");
            b.From("direct:nothing").Bean(bean, "NothingAsync");
            b.From("direct:fail").Bean(bean, "FailAsync");
            b.From("direct:null").Bean(bean, "NullAsync");
        });

        Assert.Equal("ABC", await Template.RequestBodyAsync("direct:upper", "abc"));
        Assert.Equal("keep", await Template.RequestBodyAsync("direct:record", "keep"));
        Assert.Equal(["keep"], bean.Calls);
        Assert.Equal(4, await Template.RequestBodyAsync("direct:length", "four"));
        Assert.Equal("same", await Template.RequestBodyAsync("direct:nothing", "same"));
        Assert.Null(await Template.RequestBodyAsync("direct:null", "x"));
        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:fail", "now"));
        Assert.Equal("async failure now", Assert.IsType<InvalidOperationException>(e.InnerException).Message);
    }

    [Fact]
    public async Task Void_methods_keep_the_body_and_results_replace_it_for_in_only_too()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:void").Bean(new VoidBean(), "Touch").To("mock:void");
            b.From("direct:null").Bean(new VoidBean(), "ReturnsNull").To("mock:null");
        });
        var touched = Mock("mock:void").ExpectedBodiesReceived("unchanged");
        touched.ExpectedHeaderReceived("touched", true);
        var nulled = Mock("mock:null").ExpectedBodiesReceived([null]);

        await Template.SendBodyAsync("direct:void", "unchanged");
        await Template.SendBodyAsync("direct:null", "gone");

        await touched.AssertIsSatisfiedAsync();
        await nulled.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Exceptions_thrown_by_the_bean_are_not_wrapped()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new VoidBean(), "Throw"));

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:in", "x"));

        Assert.Equal("bean failure", Assert.IsType<InvalidOperationException>(e.InnerException).Message);
    }

    [Fact]
    public async Task Values_that_cannot_be_converted_fail_with_a_parameter_binding_exception()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new AttributeBean(), "All"));

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAndHeaderAsync("direct:in", "x", "count", "many"));

        var binding = Assert.IsType<ParameterBindingException>(e.InnerException);
        Assert.Equal("count", binding.Parameter.Name);
        Assert.Equal("many", binding.Value);
    }

    [Fact]
    public async Task Static_classes_have_their_static_methods_called()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(typeof(StaticBean), "Twice"));

        Assert.Equal("abab", await Template.RequestBodyAsync("direct:in", "ab"));
    }

    [Fact]
    public async Task A_processor_bean_without_method_processes_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(new ProcessorBean()));

        Assert.Equal("processed x", await Template.RequestBodyAsync("direct:in", "x"));
    }

    [Fact]
    public async Task Registry_beans_are_looked_up_by_name()
    {
        Context.Registry.Bind("greeter", new Greeter { Prefix = "Hi" });
        Context.PropertiesComponent.InitialProperties["beanName"] = "greeter";
        await StartWithRoutes(b =>
        {
            b.From("direct:in").Bean("greeter", "Greet");
            b.From("direct:placeholder").Bean("{{beanName}}");
        });

        Assert.Equal("Hi a", await Template.RequestBodyAsync("direct:in", "a"));
        Assert.Equal("Hi b", await Template.RequestBodyAsync("direct:placeholder", "b"));
    }

    [Fact]
    public async Task A_missing_singleton_registry_bean_fails_route_creation_but_prototypes_fail_per_call()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:in").Bean("missing", scope: BeanScope.Prototype)));
        await Context.StartAsync();
        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:in", "x"));
        Assert.IsType<NoSuchBeanException>(e.InnerException);

        var created = await Assert.ThrowsAsync<FailedToCreateRouteException>(() =>
            Context.AddRoutesAsync(RouteBuilder.Create(b => b.From("direct:other").Bean("missing"))));
        Assert.IsType<NoSuchBeanException>(created.InnerException);
    }

    [Fact]
    public async Task Type_beans_follow_their_scope()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:singleton").Bean(typeof(InstanceIdBean), "WhoAmI");
            b.From("direct:prototype")
                .Bean(typeof(InstanceIdBean), "WhoAmI", BeanScope.Prototype).SetHeader("first", Expr.Body())
                .Bean(typeof(InstanceIdBean), "WhoAmI", BeanScope.Prototype).SetHeader("second", Expr.Body())
                .SetBody(Expr.Simple("${header.first == ${header.second}}"));
            b.From("direct:request")
                .Bean(typeof(InstanceIdBean), "WhoAmI", BeanScope.Request).SetHeader("first", Expr.Body())
                .Bean(typeof(InstanceIdBean), "WhoAmI", BeanScope.Request).SetHeader("second", Expr.Body())
                .SetBody(Expr.Simple("${header.first}:${header.second}"));
        });

        Assert.Equal(await Template.RequestBodyAsync("direct:singleton", null), await Template.RequestBodyAsync("direct:singleton", null));
        Assert.Equal(false, await Template.RequestBodyAsync("direct:prototype", null));
        var one = ((string)(await Template.RequestBodyAsync("direct:request", null))!).Split(':');
        var two = ((string)(await Template.RequestBodyAsync("direct:request", null))!).Split(':');
        Assert.Equal(one[0], one[1]);
        Assert.NotEqual(one[0], two[0]);
    }

    [Fact]
    public async Task Beans_come_from_the_service_provider_or_are_created_with_injected_dependencies()
    {
        var registered = new Greeter { Prefix = "From DI" };
        var services = new DictionaryServiceProvider(new()
        {
            [typeof(Greeter)] = registered,
            [typeof(Dependency)] = new Dependency { Name = "injected" },
        });
        await using var context = new CaravanContext(services) { Name = "di" };
        context.AddRoutes(RouteBuilder.Create(b =>
        {
            b.From("direct:registered").Bean(typeof(Greeter));
            b.From("direct:injected").Bean(typeof(InjectedBean));
            b.From("direct:byName").Bean(typeof(Greeter).FullName!, "Greet");
        }));
        await context.StartAsync();
        await using var template = context.CreateProducerTemplate();

        Assert.Equal("From DI x", await template.RequestBodyAsync("direct:registered", "x"));
        Assert.Equal("y with injected in di", await template.RequestBodyAsync("direct:injected", "y"));
        Assert.Equal("From DI z", await template.RequestBodyAsync("direct:byName", "z"));
    }

    [Fact]
    public async Task Types_whose_constructor_cannot_be_satisfied_fail_on_first_use()
    {
        await StartWithRoutes(b => b.From("direct:in").Bean(typeof(InjectedBean), scope: BeanScope.Prototype));

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:in", "x"));

        Assert.IsType<InvalidOperationException>(e.InnerException);
    }

    [Fact]
    public void Bean_definition_label_and_dsl_overloads()
    {
        var route = RouteBuilder.Create(b => b.From("direct:in")
            .Bean(new Greeter())
            .Bean(typeof(Greeter), "Greet")
            .Bean("name", "Greet(${body})", BeanScope.Request)
            .Bean(typeof(StaticBean)))
            .Build(Context).Single();

        Assert.Equal(["bean[Greeter]", "bean[Greeter?method=Greet]", "bean[name?method=Greet(${body})]", "bean[StaticBean]"], route.Outputs.Select(o => o.Label));
        Assert.Equal(BeanScope.Request, ((Model.BeanDefinition)route.Outputs[2]).Scope);
        Assert.Equal(typeof(StaticBean), ((Model.BeanDefinition)route.Outputs[3]).BeanType);
    }
}
