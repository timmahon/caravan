using Caravan.Builder;
using Caravan.TypeConversion;

namespace Caravan.Core.Tests;

public class ExpressionTests : ContextTestBase
{
    private Exchange NewExchange(object? body = null)
    {
        var exchange = new Exchange(Context);
        exchange.Message.Body = body;
        return exchange;
    }

    [Fact]
    public void Body_header_and_property_as_typed_values()
    {
        var e = NewExchange("42");
        e.Message.SetHeader("n", "7");
        e.SetProperty("p", "3");

        Assert.Equal(42, Expr.Body<int>().Evaluate(e));
        Assert.Equal(7, Expr.HeaderAs<int>("n").Evaluate(e));
        Assert.Null(Expr.HeaderAs<int>("missing").Evaluate(e));
        Assert.Equal(3L, Expr.PropertyAs<long>("p").Evaluate(e));
        Assert.Equal(44, Expr.Body((int n) => n + 2).Evaluate(e));
    }

    [Fact]
    public void Headers_properties_and_body_type()
    {
        var e = NewExchange(5);
        e.Message.SetHeader("a", 1);
        e.SetProperty("b", 2);

        var headers = Assert.IsAssignableFrom<IDictionary<string, object?>>(Expr.Headers().Evaluate(e));
        Assert.Equal(1, headers["A"]);
        var properties = Assert.IsAssignableFrom<IDictionary<string, object?>>(Expr.Properties().Evaluate(e));
        Assert.Equal(2, properties["b"]);
        Assert.Equal(typeof(int), Expr.BodyType().Evaluate(e));
        Assert.Null(Expr.BodyType().Evaluate(NewExchange()));
    }

    [Fact]
    public void Exception_message_uses_exception_or_caught_exception()
    {
        var e = NewExchange();
        Assert.Null(Expr.ExceptionMessage().Evaluate(e));

        e.SetProperty(ExchangePropertyKeys.ExceptionCaught, new InvalidOperationException("caught"));
        Assert.Equal("caught", Expr.ExceptionMessage().Evaluate(e));

        e.Exception = new ArgumentException("current");
        Assert.Equal("current", Expr.ExceptionMessage().Evaluate(e));
    }

    [Fact]
    public void Now_uses_context_time_provider()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Context.TimeProvider = new FixedTimeProvider(now);
        var e = NewExchange();

        Assert.Equal(now, Expr.Now().Evaluate(e));
        Assert.Equal(now.ToUnixTimeMilliseconds(), Expr.SystemTimeMillis().Evaluate(e));
    }

    [Fact]
    public void Concat_append_and_prepend_skip_nulls()
    {
        var e = NewExchange("body");
        e.Message.SetHeader("h", 1);

        Assert.Equal("body-1", Expr.Concat(Expr.Body(), Expr.Constant("-"), Expr.Header("h"), Expr.Header("none")).Evaluate(e));
        Assert.Equal("body!", Expr.Body().Append("!").Evaluate(e));
        Assert.Equal("<body", Expr.Body().Prepend("<").Evaluate(e));
        Assert.Equal("1body", Expr.Body().Prepend(Expr.Header("h")).Evaluate(e));
    }

    [Fact]
    public void Convert_converts_or_throws()
    {
        var e = NewExchange("12");

        Assert.Equal(12, Expr.Body().ConvertTo<int>().Evaluate(e));
        Assert.Null(Expr.Header("none").ConvertTo<int?>().Evaluate(e));
        Assert.Throws<NoTypeConversionAvailableException>(() => Expr.Constant("abc").ConvertTo<Guid>().Evaluate(e));
    }

    [Fact]
    public void Regex_replace_all_supports_groups_and_null()
    {
        var e = NewExchange("a1b22c333");

        Assert.Equal("a#b#c#", Expr.Body().RegexReplaceAll(@"\d+", "#").Evaluate(e));
        Assert.Equal("[1]-[22]-[333]", Expr.Constant("1,22,333").RegexReplaceAll(@"(\d+)", "[$1]").RegexReplaceAll(",", "-").Evaluate(e));
        Assert.Null(Expr.Header("none").RegexReplaceAll("x", "y").Evaluate(e));
    }

    [Fact]
    public async Task Route_id_is_the_current_route_and_restored_after_direct_call()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:a").RouteId("outer")
                .SetHeader("before", Expr.RouteId())
                .To("direct:b")
                .SetHeader("after", Expr.RouteId())
                .To("mock:result");
            b.From("direct:b").RouteId("inner").SetHeader("inside", Expr.RouteId());
        });
        var mock = Mock("result");
        mock.ExpectedHeaderReceived("before", "outer");
        mock.ExpectedHeaderReceived("inside", "inner");
        mock.ExpectedHeaderReceived("after", "outer");

        await Template.SendBodyAsync("direct:a", "x");

        await mock.AssertIsSatisfiedAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
