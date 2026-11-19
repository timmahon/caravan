using Caravan.Builder;
using Caravan.Model;
using static Caravan.Builder.Lang;

namespace Caravan.Core.Tests;

/// <summary>Deep copies of route definitions (the basis of route templates).</summary>
public class DefinitionCloneTests : ContextTestBase
{
    private static readonly Func<Exchange, bool> IsBig = e => e.Message.GetBody<int>() > 10;

    private static RouteDefinition BuildRoute()
    {
        var builder = new TestBuilder(b =>
        {
            b.ErrorHandler(b.DeadLetterChannel("mock:dead").MaximumRedeliveries(2));
            b.OnException<InvalidOperationException>(o => o.Handled(true).To("mock:error"));
            b.Intercept(i => i.To("mock:intercepted"));
            b.From("direct:start").RouteId("r").RouteProperty("key", "value")
                .Choice(c => c
                    .When(IsBig, r => r.To("mock:big").Id("big"))
                    .When(Simple("${body} == 1"), r => r.To("mock:one"))
                    .Otherwise(r => r.To("mock:small")))
                .Split(Expr.Body(), s => s.DoTry(
                    t => t.To("mock:try"),
                    c => c.DoCatch<ArgumentException>(x => x.To("mock:catch")).DoFinally(f => f.To("mock:finally"))))
                .CircuitBreaker(cb => cb.To("mock:cb"), fallback: f => f.To("mock:fallback"))
                .To("mock:end").Id("end");
        });
        return Assert.Single(builder.Build(new CaravanContext()));
    }

    [Fact]
    public void Clone_copies_every_node_and_keeps_the_tree_consistent()
    {
        var original = BuildRoute();
        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.ToString(), clone.ToString());
        Assert.Equal("r", clone.Id);
        Assert.Equal("value", clone.Properties["key"]);
        Assert.NotSame(original.Properties, clone.Properties);
        Assert.NotSame(original.Outputs, clone.Outputs);

        var originalNodes = original.AllNodes().ToList();
        var cloneNodes = clone.AllNodes().ToList();
        Assert.Equal(originalNodes.Count, cloneNodes.Count);
        Assert.True(originalNodes.Count >= 12);
        for (var i = 0; i < originalNodes.Count; i++)
        {
            Assert.NotSame(originalNodes[i], cloneNodes[i]);
            Assert.Equal(originalNodes[i].GetType(), cloneNodes[i].GetType());
            Assert.Equal(originalNodes[i].Label, cloneNodes[i].Label);
            Assert.Equal(originalNodes[i].Id, cloneNodes[i].Id);
            Assert.DoesNotContain(cloneNodes[i], originalNodes);
            if (cloneNodes[i].Parent is { } parent)
            {
                Assert.Contains(parent, cloneNodes);
            }

            if (cloneNodes[i].Route is { } route)
            {
                Assert.Same(clone, route);
            }
        }

        // Error handling and interceptors are copied too; expressions and delegates are shared.
        Assert.NotSame(original.ErrorHandler, clone.ErrorHandler);
        Assert.NotSame(((DeadLetterChannelDefinition)original.ErrorHandler!).RedeliveryPolicy, ((DeadLetterChannelDefinition)clone.ErrorHandler!).RedeliveryPolicy);
        Assert.Equal(2, ((DeadLetterChannelDefinition)clone.ErrorHandler).RedeliveryPolicy.MaximumRedeliveries);
        Assert.NotSame(original.OnExceptions[0], clone.OnExceptions[0]);
        Assert.NotSame(original.OnExceptions[0].Outputs[0], clone.OnExceptions[0].Outputs[0]);
        Assert.NotSame(original.Intercepts[0], clone.Intercepts[0]);
        var originalChoice = (ChoiceDefinition)original.Outputs[0];
        var clonedChoice = (ChoiceDefinition)clone.Outputs[0];
        Assert.Same(originalChoice.WhenClauses[0].Predicate, clonedChoice.WhenClauses[0].Predicate);
        Assert.Same(originalChoice.WhenClauses[1].Predicate, clonedChoice.WhenClauses[1].Predicate);
        Assert.Same(clonedChoice, clonedChoice.WhenClauses[0].Parent);
        Assert.Same(clonedChoice, clonedChoice.Otherwise!.Parent);
    }

    [Fact]
    public void Changing_a_clone_leaves_the_original_unchanged()
    {
        var original = BuildRoute();
        var before = Describe(original);
        var clone = original.Clone();

        clone.Id = "other";
        clone.FromUri = "direct:other";
        clone.Properties["key"] = "changed";
        clone.Outputs.RemoveAt(clone.Outputs.Count - 1);
        var choice = (ChoiceDefinition)clone.Outputs[0];
        choice.WhenClauses.RemoveAt(0);
        choice.Otherwise!.Outputs.Add(new ToDefinition("mock:added"));
        ((ToDefinition)choice.WhenClauses[0].Outputs[0]).Uri = "mock:changed";
        var tryDefinition = (TryDefinition)((SplitDefinition)clone.Outputs[1]).Outputs[0];
        tryDefinition.Catches.Clear();
        tryDefinition.Finally = null;
        ((CircuitBreakerDefinition)clone.Outputs[2]).OnFallback!.Outputs.Clear();
        clone.OnExceptions[0].Outputs.Clear();
        clone.OnExceptions[0].RedeliveryPolicy.MaximumRedeliveries = 9;
        ((DeadLetterChannelDefinition)clone.ErrorHandler!).RedeliveryPolicy.MaximumRedeliveries = 9;
        clone.Intercepts.Clear();

        Assert.Equal(before, Describe(original));
        Assert.NotEqual(before, Describe(clone));
    }

    [Fact]
    public void A_cloned_node_has_no_parent_or_route()
    {
        var route = BuildRoute();
        var when = ((ChoiceDefinition)route.Outputs[0]).WhenClauses[0];
        Assert.NotNull(when.Parent);

        var clone = (WhenDefinition)when.Clone();

        Assert.Null(clone.Parent);
        Assert.Null(clone.Route);
        Assert.Same(clone, clone.Outputs[0].Parent);
        Assert.Same(when.Predicate, clone.Predicate);
    }

    [Fact]
    public async Task A_cloned_route_runs_independently()
    {
        var original = BuildRoute();
        var clone = original.Clone();
        clone.Id = "copy";
        clone.FromUri = "direct:copy";
        clone.Outputs.Clear();
        clone.Outputs.Add(new ToDefinition("mock:copy"));
        Context.AddRoutes(new FixedRoutes(original, clone));
        await Context.StartAsync();

        await Template.SendBodyAsync("direct:start", 20);
        await Template.SendBodyAsync("direct:copy", 20);

        Assert.Equal(1, Mock("big").ReceivedCounter);
        Assert.Equal(1, Mock("copy").ReceivedCounter);
        Assert.True(Mock("intercepted").ReceivedCounter >= 2);
    }

    private static string Describe(RouteDefinition route) =>
        string.Join(
            "|",
            new[] { route.Id, route.FromUri, route.Properties["key"]?.ToString(), route.ToString(), route.OnExceptions[0].Outputs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                .Concat(route.AllNodes().Select(n => $"{n.GetType().Name}:{n.Label}:{n.Children.Count()}"))
                .Concat(route.OnExceptions.Select(o => o.RedeliveryPolicy.MaximumRedeliveries?.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .Append(((DeadLetterChannelDefinition)route.ErrorHandler!).RedeliveryPolicy.MaximumRedeliveries?.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(route.Intercepts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private sealed class TestBuilder(Action<RouteBuilder> configure) : RouteBuilder
    {
        public override void Configure() => configure(this);
    }

    private sealed class FixedRoutes(params RouteDefinition[] routes) : IRoutesBuilder
    {
        public IReadOnlyList<RouteDefinition> Build(ICaravanContext context) => routes;
    }
}
