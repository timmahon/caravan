using Caravan.Builder;

namespace Caravan.Core.Tests;

public class PropertyAndHeaderEipTests : ContextTestBase
{
    [Fact]
    public async Task Set_property_with_expression_function_and_typed_body()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .SetProperty("constant", Expr.Constant(1))
            .SetProperty("id", e => e.ExchangeId)
            .SetProperty("length", (string s) => s.Length)
            .SetHeader("upper", (string s) => s.ToUpperInvariant())
            .To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedPropertyReceived("constant", 1);
        mock.ExpectedPropertyReceived("length", 5);
        mock.ExpectedHeaderReceived("upper", "HELLO");

        await Template.SendBodyAsync("direct:start", "hello");

        await mock.AssertIsSatisfiedAsync();
        var exchange = mock.ReceivedExchanges[0];
        Assert.Equal(exchange.ExchangeId, exchange.GetProperty("id"));
    }

    [Fact]
    public async Task Remove_header_and_headers_with_excludes()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .RemoveHeader("single")
            .RemoveHeaders("tmp*", "tmpKeep")
            .RemoveHeaders("x-[0-9]+")
            .To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);

        await Template.SendBodyAndHeadersAsync("direct:start", "b", new Dictionary<string, object?>
        {
            ["Single"] = 1,
            ["tmpA"] = 2,
            ["TMPb"] = 3,
            ["tmpKeep"] = 4,
            ["x-12"] = 5,
            ["x-y"] = 6,
            ["other"] = 7,
        });

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(new[] { "tmpKeep", "x-y", "other" }, mock.ReceivedExchanges[0].Message.Headers.Keys);
    }

    [Fact]
    public async Task Remove_property_and_properties_with_excludes()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .SetProperty("a", Expr.Constant(1))
            .SetProperty("app.one", Expr.Constant(2))
            .SetProperty("app.two", Expr.Constant(3))
            .SetProperty("app.keep", Expr.Constant(4))
            .RemoveProperty("a")
            .RemoveProperties("app.*", "app.keep")
            .To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "b");

        await mock.AssertIsSatisfiedAsync();
        var properties = mock.ReceivedExchanges[0].Properties;
        Assert.False(properties.ContainsKey("a"));
        Assert.False(properties.ContainsKey("app.one"));
        Assert.False(properties.ContainsKey("app.two"));
        Assert.Equal(4, properties["app.keep"]);
    }

    [Fact]
    public async Task Convert_header_to_type_in_place_or_to_new_name()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ConvertHeaderTo("n", typeof(int))
            .ConvertHeaderTo("n", typeof(string), toName: "s")
            .ConvertHeaderTo("missing", typeof(int))
            .To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);

        await Template.SendBodyAndHeaderAsync("direct:start", "b", "n", "42");

        await mock.AssertIsSatisfiedAsync();
        var message = mock.ReceivedExchanges[0].Message;
        Assert.Equal(42, message.GetHeader("n"));
        Assert.Equal("42", message.GetHeader("s"));
        Assert.False(message.HasHeader("missing"));
    }

    [Fact]
    public async Task Convert_header_failure_is_mandatory_by_default()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:strict").ConvertHeaderTo("n", typeof(int));
            b.From("direct:lenient").ConvertHeaderTo("n", typeof(int), mandatory: false).To("mock:result");
        });
        Mock("result").ExpectedMessageCount(1);

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:strict", "b", "n", "abc"));
        Assert.IsType<TypeConversion.NoTypeConversionAvailableException>(ex.InnerException);
        await Template.SendBodyAndHeaderAsync("direct:lenient", "b", "n", "abc");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.True(Mock("result").ReceivedExchanges[0].Message.HasHeader("n"));
        Assert.Null(Mock("result").ReceivedExchanges[0].Message.GetHeader("n"));
    }
}
