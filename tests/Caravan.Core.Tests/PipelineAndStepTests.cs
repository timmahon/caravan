using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Model;

namespace Caravan.Core.Tests;

public class PipelineAndStepTests : ContextTestBase
{
    [Fact]
    public async Task Pipeline_runs_nested_steps_in_sequence()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Pipeline(p => p.SetBody(e => e.Message.Body + "1").SetBody(e => e.Message.Body + "2"))
            .SetBody(e => e.Message.Body + "3")
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("x123");

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Step_sets_step_id_while_inside_and_restores_it()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Step("outer", s => s
                .SetHeader("in-outer", Expr.Property(ExchangePropertyKeys.StepId))
                .Step("inner", i => i.SetHeader("in-inner", Expr.Property(ExchangePropertyKeys.StepId)))
                .SetHeader("back-in-outer", Expr.Property(ExchangePropertyKeys.StepId)))
            .SetHeader("after", Expr.Property(ExchangePropertyKeys.StepId))
            .To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedHeaderReceived("in-outer", "outer");
        mock.ExpectedHeaderReceived("in-inner", "inner");
        mock.ExpectedHeaderReceived("back-in-outer", "outer");
        mock.ExpectedHeaderReceived("after", null);

        await Template.SendBodyAsync("direct:start", "x");

        await mock.AssertIsSatisfiedAsync();
        Assert.False(mock.ReceivedExchanges[0].Properties.ContainsKey(ExchangePropertyKeys.StepId));
    }

    [Fact]
    public async Task Step_without_id_gets_a_generated_one()
    {
        await StartWithRoutes(b => b.From("direct:start").Step(s => s.SetHeader("step", Expr.Property(ExchangePropertyKeys.StepId))).To("mock:result"));
        Mock("result").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        var stepNode = Context.RouteDefinitions[0].AllNodes().OfType<StepDefinition>().Single();
        Assert.NotNull(stepNode.Id);
        Assert.Equal(stepNode.Id, Mock("result").ReceivedExchanges[0].Message.GetHeader("step"));
    }

    [Fact]
    public async Task Failure_inside_step_stops_the_route()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Step("s", s => s.Process(_ => throw new InvalidOperationException("boom")).To("mock:never"))
            .To("mock:after"));
        Mock("never").ExpectedMessageCount(0);
        Mock("after").ExpectedMessageCount(0);

        var result = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.IsType<InvalidOperationException>(result.Exception);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public void Nested_blocks_are_visible_in_the_model()
    {
        var builder = RouteBuilder.Create(b => b.From("direct:start")
            .Pipeline(p => p.To("mock:a"))
            .Step("s", s => s.Filter(Pred.Constant(true), f => f.To("mock:b"))));

        var route = builder.Build(Context).Single();

        Assert.Equal(new[] { "pipeline", "to", "step", "filter", "to" }, route.AllNodes().Select(n => n.ShortName));
        Assert.Equal("s", route.AllNodes().OfType<StepDefinition>().Single().Id);
    }
}
