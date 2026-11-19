using Caravan.Builder;
using Caravan.Language.Simple;

namespace Caravan.Core.Tests.Languages;

public class SimpleParseErrorTests : SimpleTestBase
{
    [Theory]
    [InlineData("${nope}", 2)]
    [InlineData("Hello ${nope}", 8)]
    [InlineData("${body", 0)]
    [InlineData("a ${body} ${header.x", 10)]
    [InlineData("${}", 0)]
    [InlineData("${ }", 0)]
    [InlineData("${header.}", 2)]
    [InlineData("${header[}", 2)]
    [InlineData("${headerAs(foo)}", 2)]
    [InlineData("${bodyAs()}", 2)]
    [InlineData("${body.}", 7)]
    [InlineData("${body.Name(}", 11)]
    [InlineData("${body.Items[}", 12)]
    [InlineData("${body.Items[]}", 12)]
    [InlineData("${body%}", 2)]
    [InlineData("${random(a)}", 2)]
    [InlineData("${random(5,1)}", 2)]
    [InlineData("${empty(foo)}", 2)]
    [InlineData("${collate(0)}", 2)]
    [InlineData("${skip(x)}", 2)]
    [InlineData("${join(a,b,c,d)}", 2)]
    [InlineData("${uuid:nope}", 2)]
    [InlineData("${properties:}", 2)]
    [InlineData("${type:NoDot}", 2)]
    [InlineData("${bean:foo?nope=1}", 2)]
    [InlineData("${header.foo > }", 13)]
    [InlineData("${header.foo ? 'a'}", 13)]
    [InlineData("${header.foo ? : 'b'}", 13)]
    [InlineData("${header.foo ?: }", 13)]
    [InlineData("${body} ${header.foo == 'x}", 8)]
    public void Malformed_expressions_report_the_position(string text, int index)
    {
        var e = Assert.Throws<SimpleParseException>(() => Expr.Simple(text));
        Assert.Equal(index, e.Index);
        Assert.Equal(text, e.Expression);
        Assert.EndsWith(text + Environment.NewLine + new string(' ', index) + "*", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_function_message_names_the_function()
    {
        var e = Assert.Throws<SimpleParseException>(() => Expr.Simple("${foo.bar}"));

        Assert.StartsWith("Unknown function: foo.bar at location 2", e.Message, StringComparison.Ordinal);
        Assert.Equal("Unknown function: foo.bar", e.Reason);
    }

    [Fact]
    public void Parse_errors_in_dsl_strings_are_thrown_when_routes_are_built()
    {
        Assert.Throws<SimpleParseException>(() => RouteBuilder.Create(b => b.From("direct:a").ToD("mock:${nope}")).Build(Context));
        Assert.Throws<SimpleParseException>(() => RouteBuilder.Create(b => b.From("direct:a").WireTap("mock:${header.}")).Build(Context));
    }

    [Fact]
    public async Task Parse_errors_in_log_messages_fail_route_creation()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:a").Log("${body")));

        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());
        Assert.IsType<SimpleParseException>(e.InnerException);
    }
}
