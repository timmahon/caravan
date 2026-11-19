using System.Text;
using Caravan.Builder;
using Caravan.DataTypes;
using static Caravan.Builder.Lang;

namespace Caravan.Core.Tests;

/// <summary>Data types: transformers, validators, route contracts (<c>inputType</c>/<c>outputType</c>) and <c>transform</c> with <c>toType</c>.</summary>
public class DataTypeTests : ContextTestBase
{
    [Fact]
    public void Data_types_parse_compare_and_match()
    {
        var json = DataType.Parse("json:Order");
        Assert.Equal("json", json.Scheme);
        Assert.Equal("Order", json.Name);
        Assert.Equal(json, DataType.Parse("json:Order"));
        Assert.Equal("json", DataType.Parse("json").ToString());
        Assert.True(DataType.Parse("json").Matches(json));
        Assert.False(json.Matches(DataType.Parse("json:Other")));
        Assert.Equal("clr:Caravan.Core.Tests.Order", DataType.Of<Order>().ToString());
        Assert.True(DataType.Parse("java:java.lang.String").IsClrType);
        Assert.False(DataType.Parse("clr").IsClrType);
    }

    [Fact]
    public void The_registry_prefers_exact_types_then_schemes_then_any_source()
    {
        var registry = Context.Transformers;
        var any = new DelegateTransformer(null, DataType.Parse("json"), _ => { });
        var schemes = new DelegateTransformer(DataType.Parse("xml"), DataType.Parse("json"), _ => { });
        var exact = new DelegateTransformer(DataType.Parse("xml:Order"), DataType.Parse("json:Order"), _ => { });
        registry.Add(any).Add(schemes).Add(exact);

        Assert.Same(exact, registry.Resolve(DataType.Parse("xml:Order"), DataType.Parse("json:Order")));
        Assert.Same(schemes, registry.Resolve(DataType.Parse("xml:Item"), DataType.Parse("json:Item")));
        Assert.Same(any, registry.Resolve(DataType.Parse("csv"), DataType.Parse("json")));
        Assert.Null(registry.Resolve(DataType.Parse("xml"), DataType.Parse("csv")));
        Assert.True(registry.Remove(any));
        Assert.Equal(2, registry.All.Count);
        Assert.Null(registry.ResolveByName("x"));
        registry.Add(new DelegateTransformer(null, null, (_, _, _) => ValueTask.CompletedTask, "x"));
        Assert.NotNull(registry.ResolveByName("x"));
    }

    [Fact]
    public async Task Input_and_output_types_transform_with_data_format_transformers()
    {
        Context.Transformers.Add(new DataFormatTransformer(DataType.Parse("json"), DataType.Of<Order>(), DataFormats.Json<Order>(), "json"));
        Context.Transformers.Add(new DataFormatTransformer(DataType.Of<Order>(), DataType.Parse("json"), DataFormats.Json(), "json"));
        await StartWithRoutes(b =>
        {
            b.From("direct:start").SetBody(Constant("{\"id\":\"1\",\"total\":2.5}")).Transform(DataType.Parse("json:Order")).To("direct:orders");
            b.From("direct:orders").InputType(typeof(Order)).OutputType("json")
                .Process(e => e.Message.Body = ((Order)e.Message.Body!) with { Total = 10 })
                .To("mock:orders");
        });

        var reply = await Template.RequestBodyAsync("direct:start", "x");

        var order = Assert.IsType<Order>(Assert.Single(Mock("orders").ReceivedBodies));
        Assert.Equal(new Order("1", 10), order);
        Assert.Equal(DataType.Of<Order>(), Mock("orders").ReceivedExchanges[0].Message.DataType);
        Assert.Equal("{\"id\":\"1\",\"total\":10}", Encoding.UTF8.GetString((byte[])reply!));
    }

    [Fact]
    public async Task Clr_types_are_converted_with_the_type_converters()
    {
        await StartWithRoutes(b => b.From("direct:start").InputType(typeof(int)).Process(e => Assert.IsType<int>(e.Message.Body)).To("mock:result"));

        await Template.SendBodyAsync("direct:start", "42");

        Assert.Equal([42], Mock("result").ReceivedBodies);
    }

    [Fact]
    public async Task Endpoint_and_lambda_transformers_and_chains()
    {
        Context.Transformers.Add(new EndpointTransformer(DataType.Parse("csv"), DataType.Parse("xml"), "direct:csvToXml"));
        Context.Transformers.Add("xml", "json", m => m.Body = "json(" + m.Body + ")");
        await StartWithRoutes(b =>
        {
            b.From("direct:csvToXml").SetBody(Simple("<x>${body}</x>"));
            b.From("direct:start").Transform(DataType.Parse("json"), DataType.Parse("csv")).To("mock:result");
            b.From("direct:xml").Transform(DataType.Parse("xml"), DataType.Parse("csv")).To("mock:xml");
        });

        await Template.SendBodyAsync("direct:start", "a,b");
        await Template.SendBodyAsync("direct:xml", "a,b");

        Assert.Equal(["json(<x>a,b</x>)"], Mock("result").ReceivedBodies);
        Assert.Equal(DataType.Parse("json"), Mock("result").ReceivedExchanges[0].Message.DataType);
        Assert.Equal(["<x>a,b</x>"], Mock("xml").ReceivedBodies);
    }

    [Fact]
    public async Task A_missing_transformer_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").Transform(DataType.Parse("json"), DataType.Parse("xml")).To("mock:result"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.Contains("No Transformer found for [xml] -> [json]", exchange.Exception!.Message);
        Assert.Empty(Mock("result").ReceivedExchanges);
    }

    [Fact]
    public async Task Same_type_and_unknown_source_types_need_no_transformer()
    {
        await StartWithRoutes(b => b.From("direct:start").InputType("json:Order").Transform(DataType.Parse("json:Order")).To("mock:result"));

        await Template.SendBodyAsync("direct:start", "{}");

        Assert.Equal(DataType.Parse("json:Order"), Mock("result").ReceivedExchanges[0].Message.DataType);
    }

    [Fact]
    public async Task Validation_uses_the_validator_of_the_type_and_fails_without_one()
    {
        Context.Validators.Add("json:Order", Pred.Simple("${body} contains 'id'"));
        await StartWithRoutes(b =>
        {
            b.From("direct:in").InputType("json:Order", validate: true).To("mock:in");
            b.From("direct:out").OutputType("xml:Order", validate: true).To("mock:out");
        });

        await Template.SendBodyAsync("direct:in", "{\"id\":1}");
        var invalid = await Template.SendAsync("direct:in", e => e.Message.Body = "{}");
        var noValidator = await Template.SendAsync("direct:out", e => e.Message.Body = "<x/>");

        Assert.Single(Mock("in").ReceivedExchanges);
        Assert.IsType<ValidationException>(invalid.Exception);
        Assert.Contains("No Validator found for type [xml:Order]", noValidator.Exception!.Message);
        Assert.Single(Mock("out").ReceivedExchanges);
    }

    [Fact]
    public async Task Endpoint_and_delegate_validators()
    {
        Context.Validators.Add(new EndpointValidator(DataType.Parse("a"), "direct:check"));
        Context.Validators.Add(new DelegateValidator(DataType.Parse("b"), m => m.Body is "ok"));
        await StartWithRoutes(b =>
        {
            b.From("direct:check").Filter(Simple("${body} != 'ok'"), f => f.ThrowException(new InvalidOperationException("invalid")));
            b.From("direct:a").InputType("a", validate: true).To("mock:a");
            b.From("direct:b").InputType("b", validate: true).To("mock:b");
        });

        await Template.SendBodyAsync("direct:a", "ok");
        var badA = await Template.SendAsync("direct:a", e => e.Message.Body = "no");
        await Template.SendBodyAsync("direct:b", "ok");
        var badB = await Template.SendAsync("direct:b", e => e.Message.Body = "no");

        Assert.Single(Mock("a").ReceivedExchanges);
        Assert.Single(Mock("b").ReceivedExchanges);
        Assert.IsType<ValidationException>(badA.Exception);
        Assert.IsType<ValidationException>(badB.Exception);
    }

    [Fact]
    public async Task Transformers_and_validators_can_be_declared_in_a_route_builder()
    {
        await StartWithRoutes(b =>
        {
            b.Transformer(new DelegateTransformer(null, DataType.Parse("upper"), m => m.Body = ((string)m.Body!).ToUpperInvariant()));
            b.Validator(new DelegateValidator(DataType.Parse("upper"), m => m.Body is string s && s == s.ToUpperInvariant()));
            b.From("direct:start").InputType("text").OutputType("upper", validate: true).To("mock:result");
        });

        var reply = await Template.RequestBodyAsync("direct:start", "abc");

        Assert.Equal("ABC", reply);
    }

    [Fact]
    public async Task Output_types_are_not_applied_to_failed_exchanges()
    {
        var calls = 0;
        Context.Transformers.Add(new DelegateTransformer(null, DataType.Parse("out"), _ => calls++));
        await StartWithRoutes(b => b.From("direct:start").InputType("in").OutputType("out").Process(_ => throw new InvalidOperationException("boom")));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.IsType<InvalidOperationException>(exchange.Exception);
        Assert.Equal(0, calls);
    }
}
