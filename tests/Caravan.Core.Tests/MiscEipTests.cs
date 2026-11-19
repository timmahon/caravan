using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>validate, script, setExchangePattern, sort and setHeaders (mirrors Camel's ValidateSimpleTest, ScriptTest, SortBodyTest, ...).</summary>
public class MiscEipTests : ContextTestBase
{
    [Fact]
    public async Task Validate_passes_matching_exchanges_and_fails_others()
    {
        await StartWithRoutes(b => b.From("direct:start").Validate(Expr.Body().Contains("Camel")).To("mock:result"));
        Mock("result").ExpectedBodiesReceived("Hello Camel");

        await Template.SendBodyAsync("direct:start", "Hello Camel");
        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "Bye World"));

        var validation = Assert.IsType<PredicateValidationException>(ex.InnerException);
        Assert.StartsWith("Validation failed for Predicate[", validation.Message);
        Assert.NotNull(validation.Predicate);
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Validate_with_lambdas_and_onException()
    {
        await StartWithRoutes(b =>
        {
            b.OnException<PredicateValidationException>(o => o.Handled(true).To("mock:invalid"));
            b.From("direct:exchange").Validate(e => e.Message.HasHeader("id")).To("mock:result");
            b.From("direct:typed").Validate((Order o) => o.Total > 0).To("mock:result");
        });
        Mock("result").ExpectedMessageCount(2);
        Mock("invalid").ExpectedMessageCount(2);

        await Template.SendBodyAndHeaderAsync("direct:exchange", "x", "id", 1);
        await Template.SendBodyAsync("direct:exchange", "x");
        await Template.SendBodyAsync("direct:typed", new Order("1", 10));
        await Template.SendBodyAsync("direct:typed", new Order("2", 0));

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Script_evaluates_for_side_effects_only()
    {
        var calls = 0;
        await StartWithRoutes(b => b.From("direct:start")
            .Script(e =>
            {
                calls++;
                e.Message.SetHeader("scripted", true);
                return "ignored result";
            })
            .Script(Expr.Constant("also ignored"))
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("x");
        Mock("result").ExpectedHeaderReceived("scripted", true);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Set_exchange_pattern_changes_the_pattern_for_the_rest_of_the_route()
    {
        await StartWithRoutes(b => b.From("direct:start").To("mock:before").SetExchangePattern(ExchangePattern.InOut).To("mock:after"));

        await Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(ExchangePattern.InOnly, Mock("before").ReceivedExchanges[0].Pattern);
        Assert.Equal(ExchangePattern.InOut, Mock("after").ReceivedExchanges[0].Pattern);
    }

    [Fact]
    public async Task Sort_orders_the_body_naturally_or_with_a_comparer()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:natural").Sort().To("mock:natural");
            b.From("direct:tokens").Sort(Expr.Tokenize(",")).To("mock:tokens");
            b.From("direct:reverse").Sort((string x, string y) => string.CompareOrdinal(y, x)).To("mock:reverse");
            b.From("direct:comparer").Sort(Expr.Body(), Comparer<object?>.Create((x, y) => ((string)x!).Length.CompareTo(((string)y!).Length))).To("mock:comparer");
        });

        await Template.SendBodyAsync("direct:natural", new List<object> { 10, 2, 33, 1 });
        await Template.SendBodyAsync("direct:tokens", "Hadrian,William,Claus");
        await Template.SendBodyAsync("direct:reverse", new[] { "a", "c", "b" });
        await Template.SendBodyAsync("direct:comparer", new[] { "ccc", "a", "bb" });

        Assert.Equal([1, 2, 10, 33], (List<object?>)Mock("natural").ReceivedBodies[0]!);
        Assert.Equal(["Claus", "Hadrian", "William"], (List<object?>)Mock("tokens").ReceivedBodies[0]!);
        Assert.Equal(["c", "b", "a"], (List<object?>)Mock("reverse").ReceivedBodies[0]!);
        Assert.Equal(["a", "bb", "ccc"], (List<object?>)Mock("comparer").ReceivedBodies[0]!);
    }

    [Fact]
    public async Task Sort_of_a_single_value_or_null()
    {
        await StartWithRoutes(b => b.From("direct:start").Sort().To("mock:result"));

        await Template.SendBodyAsync("direct:start", "single");
        await Template.SendBodyAsync("direct:start", null);

        Assert.Equal(["single"], (List<object?>)Mock("result").ReceivedBodies[0]!);
        Assert.Empty((List<object?>)Mock("result").ReceivedBodies[1]!);
    }

    [Fact]
    public async Task Set_headers_sets_several_headers_in_order()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:expressions").SetHeaders(("a", Expr.Body()), ("b", Expr.Header("a").Append("!"))).To("mock:result");
            b.From("direct:constants").SetHeaders(new Dictionary<string, object?> { ["x"] = 1, ["y"] = "two" }).To("mock:constants");
        });
        Mock("result").ExpectedHeaderReceived("a", "hi");
        Mock("result").ExpectedHeaderReceived("b", "hi!");
        Mock("constants").ExpectedHeaderReceived("x", 1);
        Mock("constants").ExpectedHeaderReceived("y", "two");

        await Template.SendBodyAsync("direct:expressions", "hi");
        await Template.SendBodyAsync("direct:constants", "x");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }
}
