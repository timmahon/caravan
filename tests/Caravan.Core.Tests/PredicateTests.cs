namespace Caravan.Core.Tests;

public class PredicateTests : ContextTestBase
{
    private Exchange NewExchange(object? body = null, params (string Name, object? Value)[] headers)
    {
        var exchange = new Exchange(Context);
        exchange.Message.Body = body;
        foreach (var (name, value) in headers)
        {
            exchange.Message.SetHeader(name, value);
        }

        return exchange;
    }

    [Theory]
    [InlineData("5", 5, true)]
    [InlineData(5, "5", true)]
    [InlineData(5, 5L, true)]
    [InlineData(5.0, 5, true)]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "ABC", false)]
    [InlineData("true", true, true)]
    [InlineData("6", 5, false)]
    [InlineData(null, null, true)]
    [InlineData(null, "x", false)]
    public void Is_equal_to_coerces_types(object? value, object? expected, bool result)
    {
        var e = NewExchange(value);

        Assert.Equal(result, Expr.Body().IsEqualTo(expected).Matches(e));
        Assert.Equal(!result, Expr.Body().IsNotEqualTo(expected).Matches(e));
    }

    [Fact]
    public void Is_equal_to_ignore_case()
    {
        Assert.True(Expr.Body().IsEqualToIgnoreCase("HELLO").Matches(NewExchange("hello")));
    }

    [Theory]
    [InlineData("10", 6, 1)]
    [InlineData(6, "10", -1)]
    [InlineData(5, 5.0, 0)]
    [InlineData(2.5, 2, 1)]
    [InlineData("b", "a", 1)]
    [InlineData(null, 1, -1)]
    public void Comparisons_coerce_numbers_before_strings(object? left, object? right, int sign)
    {
        var e = NewExchange(left);

        Assert.Equal(sign > 0, Expr.Body().IsGreaterThan(right).Matches(e));
        Assert.Equal(sign >= 0, Expr.Body().IsGreaterThanOrEqualTo(right).Matches(e));
        Assert.Equal(sign < 0, Expr.Body().IsLessThan(right).Matches(e));
        Assert.Equal(sign <= 0, Expr.Body().IsLessThanOrEqualTo(right).Matches(e));
    }

    [Fact]
    public void Comparisons_coerce_other_comparables()
    {
        var e = NewExchange(new DateTime(2026, 5, 1));

        Assert.True(Expr.Body().IsGreaterThan("2026-01-01").Matches(e));
        Assert.True(Expr.Body().IsLessThan(new DateTime(2027, 1, 1)).Matches(e));
    }

    [Fact]
    public void Right_hand_side_can_be_an_expression()
    {
        var e = NewExchange(10, ("limit", "7"));

        Assert.True(Expr.Body().IsGreaterThan(Expr.Header("limit")).Matches(e));
        Assert.False(Expr.Body().IsEqualTo(Expr.Header("limit")).Matches(e));
    }

    [Fact]
    public void Null_checks()
    {
        var e = NewExchange(null, ("h", 1));

        Assert.True(Expr.Body().IsNull().Matches(e));
        Assert.False(Expr.Body().IsNotNull().Matches(e));
        Assert.True(Expr.Header("h").IsNotNull().Matches(e));
    }

    [Fact]
    public void String_predicates()
    {
        var e = NewExchange("Hello World");

        Assert.True(Expr.Body().Contains("lo W").Matches(e));
        Assert.False(Expr.Body().Contains("xyz").Matches(e));
        Assert.True(Expr.Body().StartsWith("Hell").Matches(e));
        Assert.False(Expr.Body().StartsWith("hell").Matches(e));
        Assert.True(Expr.Body().EndsWith("World").Matches(e));
        Assert.True(Expr.Body().Regex("H.*d").Matches(e));
        Assert.False(Expr.Body().Regex("World").Matches(e));
        Assert.False(Expr.Header("none").StartsWith("x").Matches(e));
        Assert.False(Expr.Header("none").Regex(".*").Matches(e));
    }

    [Fact]
    public void Contains_checks_collection_elements_with_coercion()
    {
        var e = NewExchange(new List<int> { 1, 2, 3 });

        Assert.True(Expr.Body().Contains("2").Matches(e));
        Assert.False(Expr.Body().Contains(4).Matches(e));
    }

    [Fact]
    public void Is_instance_of_and_in()
    {
        var e = NewExchange("b");

        Assert.True(Expr.Body().IsInstanceOf<string>().Matches(e));
        Assert.False(Expr.Body().IsInstanceOf<int>().Matches(e));
        Assert.True(Expr.Body().In("a", "b", "c").Matches(e));
        Assert.False(Expr.Body().In("x", "y").Matches(e));
        Assert.True(Expr.Constant(2).In("1", "2").Matches(e));
    }

    [Fact]
    public void Combinators_short_circuit()
    {
        var e = NewExchange(5);
        var evaluated = 0;
        var counting = Pred.From(_ =>
        {
            evaluated++;
            return true;
        });

        Assert.True(Expr.Body().IsGreaterThan(1).And(Expr.Body().IsLessThan(10)).Matches(e));
        Assert.False(Pred.Constant(false).And(counting).Matches(e));
        Assert.True(Pred.Constant(true).Or(counting).Matches(e));
        Assert.Equal(0, evaluated);
        Assert.True(Pred.Not(Pred.Constant(false)).Matches(e));
        Assert.False(Pred.Constant(true).Not().Matches(e));
        Assert.True(Pred.And().Matches(e));
        Assert.False(Pred.Or().Matches(e));
        Assert.True(Pred.In(Pred.Constant(false), Expr.Body().IsEqualTo(5)).Matches(e));
    }

    [Fact]
    public void Typed_body_and_header_predicates()
    {
        var e = NewExchange(new Order("o1", 12m), ("count", "3"));

        Assert.True(Pred.Body((Order o) => o.Total > 10).Matches(e));
        Assert.True(Pred.Header("count", (int n) => n == 3).Matches(e));
        Assert.True(Pred.Header("missing", (int n) => n == 0).Matches(e));
        Assert.False(Pred.Body((int n) => n > 0).Matches(NewExchange()));
    }

    [Fact]
    public void Predicates_have_readable_text()
    {
        Assert.Equal("${header.a} == 5", Expr.Header("a").IsEqualTo(5).ToString());
        Assert.Equal("(${body} != null && !(${header.x} > 1))", Expr.Body().IsNotNull().And(Expr.Header("x").IsGreaterThan(1).Not()).ToString());
    }
}
