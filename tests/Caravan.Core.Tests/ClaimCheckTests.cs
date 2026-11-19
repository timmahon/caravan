using Caravan.Aggregation;
using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Core.Tests;

/// <summary>claimCheck semantics (mirrors Camel's ClaimCheckEip* tests).</summary>
public class ClaimCheckTests : ContextTestBase
{
    [Fact]
    public async Task Set_then_get_restores_body_and_headers_and_keeps_the_data()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .To("mock:a")
            .ClaimCheck(ClaimCheckOperation.Set, "foo")
            .Transform(_ => "Bye World").SetHeader("foo", _ => 456)
            .To("mock:b")
            .ClaimCheck(ClaimCheckOperation.Get, "foo")
            .To("mock:c")
            .Transform(_ => "Hi World")
            .To("mock:d")
            .ClaimCheck(ClaimCheckOperation.Get, "foo")
            .To("mock:e"));
        Mock("a").ExpectedBodiesReceived("Hello World");
        Mock("b").ExpectedBodiesReceived("Bye World");
        Mock("b").ExpectedHeaderReceived("foo", 456);
        Mock("c").ExpectedBodiesReceived("Hello World");
        Mock("c").ExpectedHeaderReceived("foo", 123);
        Mock("d").ExpectedBodiesReceived("Hi World");
        Mock("e").ExpectedBodiesReceived("Hello World");

        await Template.SendBodyAndHeaderAsync("direct:start", "Hello World", "foo", 123);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Get_and_remove_merges_back_once()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ClaimCheck(ClaimCheckOperation.Set, "foo")
            .Transform(_ => "Bye World")
            .ClaimCheck(ClaimCheckOperation.GetAndRemove, "foo")
            .To("mock:c")
            .Transform(_ => "Hi World")
            .ClaimCheck(ClaimCheckOperation.GetAndRemove, "foo")
            .To("mock:d"));
        Mock("c").ExpectedBodiesReceived("Hello World");
        Mock("d").ExpectedBodiesReceived("Hi World");

        await Template.SendBodyAsync("direct:start", "Hello World");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        var repository = Assert.IsAssignableFrom<IClaimCheckRepository>(Mock("d").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.ClaimCheckRepository));
        Assert.False(repository.Contains("foo"));
    }

    [Fact]
    public async Task Set_replaces_data_stored_under_the_same_key()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ClaimCheck(ClaimCheckOperation.Set, "foo")
            .Transform(_ => "second")
            .ClaimCheck(ClaimCheckOperation.Set, "foo")
            .Transform(_ => "third")
            .ClaimCheck(ClaimCheckOperation.Get, "foo")
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("second");

        await Template.SendBodyAsync("direct:start", "first");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Push_and_pop_work_as_a_stack()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ClaimCheck(ClaimCheckOperation.Push)
            .Transform(_ => "Bye World")
            .ClaimCheck(ClaimCheckOperation.Push)
            .Transform(_ => "Hi World")
            .To("mock:a")
            .ClaimCheck(ClaimCheckOperation.Pop)
            .To("mock:b")
            .ClaimCheck(ClaimCheckOperation.Pop)
            .To("mock:c")
            .ClaimCheck(ClaimCheckOperation.Pop)
            .To("mock:d"));
        Mock("a").ExpectedBodiesReceived("Hi World");
        Mock("b").ExpectedBodiesReceived("Bye World");
        Mock("c").ExpectedBodiesReceived("Hello World");
        Mock("d").ExpectedBodiesReceived("Hello World");

        await Template.SendBodyAsync("direct:start", "Hello World");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Get_of_a_missing_key_leaves_the_exchange_unchanged()
    {
        await StartWithRoutes(b => b.From("direct:start").ClaimCheck(ClaimCheckOperation.Get, "missing").To("mock:result"));
        Mock("result").ExpectedBodiesReceived("x");

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Filter_body_only_keeps_the_current_headers()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ClaimCheck(ClaimCheckOperation.Set, "foo")
            .Transform(_ => "Bye World").SetHeader("foo", _ => 456)
            .ClaimCheck(ClaimCheckOperation.Get, "foo", filter: "body")
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("Hello World");
        Mock("result").ExpectedHeaderReceived("foo", 456);

        await Template.SendBodyAndHeaderAsync("direct:start", "Hello World", "foo", 123);

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Filter_header_patterns_include_matching_headers_only()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ClaimCheck(ClaimCheckOperation.Set, "foo")
            .Transform(_ => "Bye World").SetHeader("foo", _ => 456).SetHeader("bar", _ => "Changed").SetHeader("baz", _ => "Changed")
            .ClaimCheck(ClaimCheckOperation.Get, "foo", filter: "header:foo,+header:ba*")
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("Bye World");

        await Template.SendBodyAndHeadersAsync("direct:start", "Hello World", new Dictionary<string, object?> { ["foo"] = 123, ["bar"] = "Moes", ["baz"] = "Cool" });

        await Mock("result").AssertIsSatisfiedAsync();
        var headers = Mock("result").ReceivedExchanges[0].Message.Headers;
        Assert.Equal(123, headers["foo"]);
        Assert.Equal("Moes", headers["bar"]);
        Assert.Equal("Cool", headers["baz"]);
    }

    [Fact]
    public async Task Filter_exclusions_merge_back_everything_else()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ClaimCheck(ClaimCheckOperation.Set, "foo")
            .Transform(_ => "Bye World").SetHeader("foo", _ => 456).SetHeader("bar", _ => "Changed")
            .ClaimCheck(ClaimCheckOperation.Get, "foo", filter: "-header:bar")
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("Hello World");
        Mock("result").ExpectedHeaderReceived("foo", 123);
        Mock("result").ExpectedHeaderReceived("bar", "Changed");

        await Template.SendBodyAndHeadersAsync("direct:start", "Hello World", new Dictionary<string, object?> { ["foo"] = 123, ["bar"] = "Moes" });

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Filter_headers_without_body_and_minus_headers()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:headers")
                .ClaimCheck(ClaimCheckOperation.Push)
                .Transform(_ => "Bye World").SetHeader("foo", _ => 456)
                .ClaimCheck(ClaimCheckOperation.Pop, filter: "headers")
                .To("mock:headers");
            b.From("direct:body")
                .ClaimCheck(ClaimCheckOperation.Push)
                .Transform(_ => "Bye World").SetHeader("foo", _ => 456)
                .ClaimCheck(ClaimCheckOperation.Pop, filter: "-headers")
                .To("mock:body");
        });
        Mock("headers").ExpectedBodiesReceived("Bye World");
        Mock("headers").ExpectedHeaderReceived("foo", 123);
        Mock("body").ExpectedBodiesReceived("Hello World");
        Mock("body").ExpectedHeaderReceived("foo", 456);

        await Template.SendBodyAndHeaderAsync("direct:headers", "Hello World", "foo", 123);
        await Template.SendBodyAndHeaderAsync("direct:body", "Hello World", "foo", 123);

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Key_with_tokens_and_custom_strategy()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ClaimCheck(ClaimCheckOperation.Set, "order-${header.id}")
            .Transform(_ => "new")
            .ClaimCheck(
                ClaimCheckOperation.Get,
                "order-${header.id}",
                aggregationStrategy: AggregationStrategies.From((current, stored) =>
                {
                    current!.Message.Body = $"{current.Message.Body}+{stored.Message.Body}";
                    return current;
                }))
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("new+old");

        await Template.SendBodyAndHeaderAsync("direct:start", "old", "id", 7);

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Stored_data_is_isolated_from_later_changes()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .ClaimCheck(ClaimCheckOperation.Set, "k")
            .ClaimCheck(ClaimCheckOperation.Get, "k")
            .SetHeader("h", _ => "changed after get")
            .ClaimCheck(ClaimCheckOperation.Get, "k")
            .To("mock:result"));
        Mock("result").ExpectedHeaderReceived("h", "original");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "h", "original");

        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Invalid_configuration_fails_the_route()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:nokey").ClaimCheck(ClaimCheckOperation.Get)));
        var noKey = await Assert.ThrowsAnyAsync<Exception>(() => Context.StartAsync());
        Assert.Contains("a key is required", noKey.ToString());

        await using var other = new CaravanContext();
        other.AddRoutes(RouteBuilder.Create(b => b.From("direct:badfilter").ClaimCheck(ClaimCheckOperation.Get, "k", filter: "bodies")));
        var badFilter = await Assert.ThrowsAnyAsync<Exception>(() => other.StartAsync());
        Assert.Contains("Invalid claim check filter part 'bodies'", badFilter.ToString());
    }

    [Fact]
    public void Strategy_merges_everything_without_a_filter()
    {
        var original = new Exchange(Context);
        original.Message.Body = "current";
        original.Message.SetHeader("a", 1);
        var stored = new Exchange(Context);
        stored.Message.Body = "stored";
        stored.Message.SetHeader("b", 2);

        var result = new ClaimCheckAggregationStrategy().Aggregate(original, stored)!;

        Assert.Same(original, result);
        Assert.Equal("stored", result.Message.Body);
        Assert.Equal(1, result.Message.GetHeader("a"));
        Assert.Equal(2, result.Message.GetHeader("b"));
    }
}
