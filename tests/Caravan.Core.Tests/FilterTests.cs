using Caravan.Builder;
using Caravan.Model;

namespace Caravan.Core.Tests;

public class FilterTests : ContextTestBase
{
    [Fact]
    public async Task Only_matching_exchanges_enter_the_block_and_all_continue()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Filter(Expr.Header("vip").IsEqualTo(true), f => f.To("mock:vip"))
            .To("mock:all"));
        var vip = Mock("vip");
        vip.ExpectedBodiesReceived("a");
        var all = Mock("all");
        all.ExpectedBodiesReceived("a", "b");

        await Template.SendBodyAndHeaderAsync("direct:start", "a", "vip", "true");
        await Template.SendBodyAndHeaderAsync("direct:start", "b", "vip", false);

        await vip.AssertIsSatisfiedAsync();
        await all.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Sets_filter_matched_property()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Filter(e => e.Message.GetBody<string>() == "yes", f => f.SetHeader("in", Expr.Constant(true)))
            .To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedMessageCount(2);

        await Template.SendBodyAsync("direct:start", "yes");
        await Template.SendBodyAsync("direct:start", "no");

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(true, mock.ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.FilterMatched));
        Assert.Equal(true, mock.ReceivedExchanges[0].Message.GetHeader("in"));
        Assert.Equal(false, mock.ReceivedExchanges[1].GetProperty(ExchangePropertyKeys.FilterMatched));
        Assert.False(mock.ReceivedExchanges[1].Message.HasHeader("in"));
    }

    [Fact]
    public async Task Typed_filter_on_body()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Filter((Order o) => o.Total > 100, f => f.To("mock:big")));
        var big = Mock("big");
        big.ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", new Order("1", 50));
        await Template.SendBodyAsync("direct:start", new Order("2", 500));

        await big.AssertIsSatisfiedAsync();
        Assert.Equal("2", big.GetReceivedBodies<Order>()[0]!.Id);
    }

    [Fact]
    public async Task Nested_filters_and_empty_block()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Filter(Expr.Body().IsGreaterThan(10), f => f
                .Filter(Expr.Body().IsGreaterThan(100), g => g.To("mock:huge"))
                .To("mock:big"))
            .Filter(Pred.Constant(true), _ => { })
            .To("mock:all"));
        Mock("huge").ExpectedBodiesReceived(1000);
        Mock("big").ExpectedBodiesReceived(50, 1000);
        Mock("all").ExpectedMessageCount(3);

        foreach (var n in new[] { 5, 50, 1000 })
        {
            await Template.SendBodyAsync("direct:start", n);
        }

        await Caravan.Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Predicate_exception_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Filter(_ => throw new InvalidOperationException("bad predicate"), f => f.To("mock:never"))
            .To("mock:after"));
        Mock("never").ExpectedMessageCount(0);
        Mock("after").ExpectedMessageCount(0);

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("direct:start", "x"));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        await Caravan.Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Nested_nodes_are_part_of_the_route_model()
    {
        await StartWithRoutes(b => b.From("direct:start").RouteId("r")
            .Filter(Pred.Constant(true), f => f.To("mock:a").Id("inner")).Id("outer"));

        var nodes = Context.RouteDefinitions[0].AllNodes().ToList();
        Assert.Equal(new[] { "outer", "inner" }, nodes.Select(n => n.Id));
        Assert.IsType<FilterDefinition>(nodes[1].Parent);
    }
}
