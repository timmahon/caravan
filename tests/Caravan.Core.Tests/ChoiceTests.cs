using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Model;

namespace Caravan.Core.Tests;

public class ChoiceTests : ContextTestBase
{
    private Task StartCbr() => StartWithRoutes(b => b.From("direct:start")
        .Choice(c => c
            .When(Expr.Body().StartsWith("a"), r => r.To("mock:a"))
            .When(Expr.Body().StartsWith("ab"), r => r.To("mock:ab"))
            .When(e => e.Message.GetBody<string>() == "b", r => r.To("mock:b"))
            .Otherwise(r => r.To("mock:other")))
        .To("mock:after"));

    [Fact]
    public async Task First_matching_when_wins_otherwise_catches_the_rest()
    {
        await StartCbr();
        Mock("a").ExpectedBodiesReceived("a1", "ab");
        Mock("ab").ExpectedMessageCount(0);
        Mock("b").ExpectedBodiesReceived("b");
        Mock("other").ExpectedBodiesReceived("z");
        Mock("after").ExpectedBodiesReceived("a1", "ab", "b", "z");

        foreach (var body in new[] { "a1", "ab", "b", "z" })
        {
            await Template.SendBodyAsync("direct:start", body);
        }

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Without_otherwise_unmatched_exchanges_continue()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Choice(c => c.When((string s) => s.StartsWith('x'), r => r.SetBody(_ => "matched")))
            .To("mock:after"));
        var after = Mock("after");
        after.ExpectedBodiesReceived("matched", "y");

        await Template.SendBodyAsync("direct:start", "x");
        await Template.SendBodyAsync("direct:start", "y");

        await after.AssertIsSatisfiedAsync();
        Assert.Equal(false, after.ReceivedExchanges[1].GetProperty(ExchangePropertyKeys.FilterMatched));
    }

    [Fact]
    public async Task Filter_inside_choice_and_choice_inside_filter()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Choice(c => c
                .When(Expr.Header("type").IsEqualTo("order"), r => r
                    .Filter(Expr.Body().IsGreaterThan(10), f => f.To("mock:bigOrder"))
                    .To("mock:order"))
                .Otherwise(r => r.To("mock:other")))
            .Filter(Expr.Header("type").IsNotNull(), f => f
                .Choice(c => c.When(Expr.Body().IsLessThan(10), r => r.To("mock:small")))));
        Mock("bigOrder").ExpectedBodiesReceived(20);
        Mock("order").ExpectedBodiesReceived(5, 20);
        Mock("other").ExpectedBodiesReceived(1);
        Mock("small").ExpectedBodiesReceived(5, 1);

        await Template.SendBodyAndHeaderAsync("direct:start", 5, "type", "order");
        await Template.SendBodyAndHeaderAsync("direct:start", 20, "type", "order");
        await Template.SendBodyAndHeaderAsync("direct:start", 1, "type", "other");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task When_clauses_get_node_ids_and_parents()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Choice(c => c.When(Pred.Constant(true), r => r.To("mock:a")).Id("w1").Otherwise(r => r.To("mock:b"))));

        var nodes = Context.RouteDefinitions[0].AllNodes().ToList();
        Assert.Equal(new[] { "choice", "when", "to", "otherwise", "to" }, nodes.Select(n => n.ShortName));
        Assert.All(nodes, n => Assert.NotNull(n.Id));
        Assert.Equal("w1", nodes[1].Id);
        Assert.IsType<ChoiceDefinition>(nodes[3].Parent);
        Assert.IsType<OtherwiseDefinition>(nodes[4].Parent);
    }

    [Fact]
    public void Builder_rejects_misplaced_clauses()
    {
        var route = new RouteDefinition("direct:x");
        var steps = new RouteDefinitionBuilder(route);

        Assert.Throws<InvalidOperationException>(() => steps.Choice(c => c.Otherwise(_ => { }).When(Pred.Constant(true), _ => { })));
        Assert.Throws<InvalidOperationException>(() => steps.Choice(c => c.Otherwise(_ => { }).Otherwise(_ => { })));
    }

    [Fact]
    public async Task Choice_without_when_fails_to_create_route()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:start").Choice(c => c.Otherwise(r => r.To("mock:x")))));

        await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());
    }
}
