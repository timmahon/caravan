using Caravan.Builder;
using Caravan.Language;
using Caravan.Language.Simple;
using Caravan.Spi;
using static Caravan.Builder.Lang;

namespace Caravan.Core.Tests.Languages;

[Collection(SimpleGlobalStateCollection.Name)]
public class SimpleLanguageTests : ContextTestBase
{
    [Fact]
    public void Texts_are_compiled_once_and_cached()
    {
        var text = "cached ${header.x} " + Guid.NewGuid();

        Assert.Same(SimpleLanguage.Expression(text), SimpleLanguage.Expression(text));
        Assert.Same(Expr.Simple(text), Expr.Simple(text));
        Assert.Same(Pred.Simple("${header.x} == '" + text + "'"), Pred.Simple("${header.x} == '" + text + "'"));
        Assert.NotSame(Expr.Simple(text, typeof(string)), Expr.Simple(text, typeof(string)));
    }

    [Fact]
    public void Cache_is_bounded_least_recently_used()
    {
        var cache = new LruCache<string, object>(3);
        var created = 0;
        object Create(string key) => new { key, n = ++created };

        var first = cache.GetOrAdd("a", Create);
        cache.GetOrAdd("b", Create);
        cache.GetOrAdd("c", Create);
        Assert.Same(first, cache.GetOrAdd("a", Create));
        cache.GetOrAdd("d", Create);

        Assert.Equal(3, cache.Count);
        Assert.Same(first, cache.GetOrAdd("a", Create));
        Assert.Equal(4, created);
        cache.GetOrAdd("b", Create);
        Assert.Equal(5, created);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(SimpleLanguage.DefaultCacheSize, SimpleLanguage.CacheSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => SimpleLanguage.CacheSize = 0);
        Assert.InRange(SimpleLanguage.CachedCount, 0, 2 * SimpleLanguage.CacheSize);
    }

    [Fact]
    public async Task Compiled_texts_are_evaluated_concurrently()
    {
        var expression = Expr.Simple("${header.n} -> ${body.Length} ${header.n > 50 ? 'big' : 'small'}");
        var predicate = Pred.Simple("${header.n} range '0..49' || ${body} contains 'x'");

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(n => Task.Run(() =>
        {
            var exchange = new Exchange(Context);
            exchange.Message.Body = new string('a', n);
            exchange.Message.SetHeader("n", n);
            var values = new List<(object?, bool)>();
            for (var i = 0; i < 100; i++)
            {
                values.Add((expression.Evaluate(exchange), predicate.Matches(exchange)));
            }

            return (n, values);
        })));

        foreach (var (n, values) in results)
        {
            Assert.All(values, v => Assert.Equal(($"{n} -> {n} {(n > 50 ? "big" : "small")}", n < 50), v));
        }
    }

    [Fact]
    public async Task Concurrent_compilation_of_the_same_and_different_texts_is_safe()
    {
        var texts = Enumerable.Range(0, 50).Select(i => $"${{header.h{i % 10}}}-{i % 25}").ToArray();
        var exchange = new Exchange(Context);
        exchange.Message.SetHeader("h3", "x");

        var results = await Task.WhenAll(texts.SelectMany(t => Enumerable.Repeat(t, 4)).Select(t => Task.Run(() => Expr.Simple(t).Evaluate(exchange))));

        Assert.Equal(200, results.Length);
        Assert.Contains("x-3", results);
    }

    [Fact]
    public void Expression_result_type_converts_the_value()
    {
        var exchange = new Exchange(Context);
        exchange.Message.SetHeader("n", "42");

        Assert.Equal(42, Expr.Simple("${header.n}", typeof(int)).Evaluate(exchange));
        Assert.Equal(42L, Expr.Simple<long>("${header.n}").Evaluate(exchange));
        Assert.Equal(421, Simple("${header.n}1", typeof(int)).Evaluate(exchange));
        Assert.Equal(typeof(int), ((SimpleExpression)Expr.Simple("${header.n}", typeof(int))).ResultType);
    }

    [Fact]
    public void Languages_are_resolved_by_name()
    {
        var exchange = new Exchange(Context);
        exchange.Message.Body = "a,b";
        exchange.Message.SetHeader("h", "header!");
        exchange.SetProperty("p", "property!");

        Assert.IsType<SimpleLanguage>(Context.ResolveLanguage("simple"));
        Assert.Equal("header!", Context.ResolveLanguage("simple").CreateExpression("${header.h}").Evaluate(exchange));
        Assert.True(Context.ResolveLanguage("simple").CreatePredicate("${header.h} == 'header!'").Matches(exchange));
        Assert.Equal("text", Context.ResolveLanguage("constant").CreateExpression("text").Evaluate(exchange));
        Assert.True(Context.ResolveLanguage("constant").CreatePredicate("true").Matches(exchange));
        Assert.False(Context.ResolveLanguage("constant").CreatePredicate("false").Matches(exchange));
        Assert.Equal("header!", Context.ResolveLanguage("header").CreateExpression("h").Evaluate(exchange));
        Assert.True(Context.ResolveLanguage("header").CreatePredicate("h").Matches(exchange));
        Assert.Equal("property!", Context.ResolveLanguage("exchangeProperty").CreateExpression("p").Evaluate(exchange));
        Assert.Equal(["a", "b"], ((IEnumerable<object>)Context.ResolveLanguage("tokenize").CreateExpression(",").Evaluate(exchange)!).Cast<string>());
        Assert.Contains("simple", Context.Languages.Names);
        Assert.Throws<NoSuchLanguageException>(() => Context.ResolveLanguage("groovy"));
    }

    [Fact]
    public void Ref_language_evaluates_registry_beans()
    {
        var exchange = new Exchange(Context);
        exchange.Message.SetHeader("x", 7);
        Context.Registry.Bind("myExpression", Expr.Header("x"));
        Context.Registry.Bind("myPredicate", Pred.Simple("${header.x} > 5"));
        Context.Registry.Bind("notAnExpression", new object());
        var language = Context.ResolveLanguage("ref");

        Assert.Equal(7, language.CreateExpression("myExpression").Evaluate(exchange));
        Assert.Equal(true, language.CreateExpression("myPredicate").Evaluate(exchange));
        Assert.True(language.CreatePredicate("myPredicate").Matches(exchange));
        Assert.True(language.CreatePredicate("myExpression").Matches(exchange));
        Assert.Throws<NoSuchBeanException>(() => language.CreateExpression("missing").Evaluate(exchange));
        Assert.Throws<InvalidOperationException>(() => language.CreatePredicate("notAnExpression").Matches(exchange));
    }

    [Fact]
    public void Custom_languages_are_added_per_context_or_in_the_registry()
    {
        var upper = new UpperLanguage();
        Context.Languages.Add("upper", upper);
        Context.Registry.Bind("lower", new LowerLanguage());
        var exchange = new Exchange(Context);

        Assert.Same(upper, Context.ResolveLanguage("upper"));
        Assert.Equal("ABC", Context.ResolveLanguage("upper").CreateExpression("abc").Evaluate(exchange));
        Assert.Equal("abc", Context.ResolveLanguage("lower").CreateExpression("ABC").Evaluate(exchange));
        Assert.Equal("ABC", Lang.Language("upper", "abc").Evaluate(exchange));
        Assert.Throws<NoSuchLanguageException>(() => new CaravanContext().ResolveLanguage("upper"));
    }

    [Fact]
    public async Task Simple_works_throughout_the_dsl()
    {
        Context.PropertiesComponent.InitialProperties["threshold"] = "100";
        await StartWithRoutes(b => b.From("direct:start")
            .SetHeader("total", Simple("${body.Total}"))
            .Filter(Simple("${header.total} > {{threshold}}"), f => f
                .SetBody(Simple("Order ${body.Id}: ${header.total}"))
                .Choice(c => c
                    .When(Simple("${header.total} >= 1000"), r => r.SetHeader("size", Constant("huge")))
                    .Otherwise(r => r.SetHeader("size", Simple("${header.total > 500 ? 'large' : 'medium'}"))))
                .Log("Big order ${body} ({{threshold}})")
                .ToD("mock:${header.size}")));
        Mock("mock:medium").ExpectedBodiesReceived("Order o2: 200");
        Mock("mock:large").ExpectedBodiesReceived("Order o3: 600");
        Mock("mock:huge").ExpectedBodiesReceived("Order o4: 5000");

        foreach (var order in new[] { new Order("o1", 50m), new Order("o2", 200m), new Order("o3", 600m), new Order("o4", 5000m) })
        {
            await Template.SendBodyAsync("direct:start", order);
        }

        await Mock("mock:medium").AssertIsSatisfiedAsync();
        await Mock("mock:large").AssertIsSatisfiedAsync();
        await Mock("mock:huge").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Malformed_simple_predicates_fail_route_creation()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:start").Filter(Simple("${header.x} =="), f => f.To("mock:a"))));

        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());
        var parse = Assert.IsType<SimpleParseException>(e.InnerException);
        Assert.Equal(12, parse.Index);
    }

    [Fact]
    public void Simple_builder_is_an_expression_and_a_predicate()
    {
        var exchange = new Exchange(Context);
        exchange.Message.SetHeader("x", 10);
        var builder = Simple("${header.x} > 5");

        Assert.Equal("10 > 5", builder.Evaluate(exchange));
        Assert.True(builder.Matches(exchange));
        Assert.Equal("${header.x} > 5", builder.Text);
        Assert.Same(builder.AsPredicate(), builder.AsPredicate());
        Assert.Equal("simple{${header.x} > 5}", builder.ToString());
        Assert.Throws<SimpleParseException>(() => Simple("${header.x} ==").Matches(exchange));
        Assert.Throws<SimpleParseException>(() => Simple("${nope}"));
    }

    private sealed class UpperLanguage : ILanguage
    {
        public IExpression CreateExpression(string expression) => Expr.Constant(expression.ToUpperInvariant());

        public IPredicate CreatePredicate(string expression) => Pred.Constant(true);
    }

    private sealed class LowerLanguage : ILanguage
    {
        public IExpression CreateExpression(string expression) => Expr.Constant(expression.ToLowerInvariant());

        public IPredicate CreatePredicate(string expression) => Pred.Constant(false);
    }
}
