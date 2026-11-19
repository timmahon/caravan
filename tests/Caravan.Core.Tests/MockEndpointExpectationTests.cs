using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

/// <summary>Mock endpoint expectations (mirrors Camel's MockEndpointTest).</summary>
public class MockEndpointExpectationTests : ContextTestBase
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await StartWithRoutes(b => b.From("direct:start").To("mock:result"));
    }

    [Fact]
    public async Task Message_clauses_check_headers_bodies_and_properties()
    {
        var mock = Mock("result");
        mock.Message(0).Header("type").IsEqualTo("order").Body<int>().IsGreaterThan(10).Property("p").IsNull();
        mock.Message(1).Body().IsEqualTo("5").Header("type").IsNotNull().Matches(e => e.Message.HasHeader("type"), "has type");

        await Template.SendBodyAndHeaderAsync("direct:start", "42", "type", "order");
        await Template.SendBodyAndHeaderAsync("direct:start", 5, "type", "line");

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Message_clause_reports_the_failing_condition()
    {
        var mock = Mock("result");
        mock.Message(0).Body<int>().IsLessThan(10);

        await Template.SendBodyAsync("direct:start", "42");

        var e = await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
        Assert.Contains("Message 0: expected body as Int32 to be less than <10> but was <42>", e.Message);
    }

    [Fact]
    public async Task Message_clause_fails_when_the_message_was_not_received()
    {
        var mock = Mock("result");
        mock.Message(1).Body().IsNotNull();

        await Template.SendBodyAsync("direct:start", "a");

        var e = await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
        Assert.Contains("Message 1 was not received", e.Message);
    }

    [Fact]
    public async Task AllMessages_clause_checks_every_message()
    {
        var mock = Mock("result");
        mock.AllMessages().Header("tenant").IsNotNull().Body().StartsWith("o");

        await Template.SendBodyAndHeaderAsync("direct:start", "one", "tenant", "t1");
        await mock.AssertIsSatisfiedAsync();

        await Template.SendBodyAsync("direct:start", "other");
        var e = await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
        Assert.Contains("Message 1: expected header tenant to be not null", e.Message);
    }

    [Fact]
    public async Task ExpectedBodyReceived_checks_the_body_of_every_message()
    {
        var mock = Mock("result");
        mock.ExpectedMessageCount(2);
        mock.ExpectedBodyReceived().Regex("^Hello");

        await Template.SendBodyAsync("direct:start", "Hello World");
        await Template.SendBodyAsync("direct:start", "Hello Caravan");
        await mock.AssertIsSatisfiedAsync();

        mock.Reset();
        mock.ExpectedBodyReceived().IsEqualTo("Hello");
        await Template.SendBodyAsync("direct:start", "Bye");
        await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
    }

    [Fact]
    public async Task ExpectsAscending_and_descending()
    {
        var mock = Mock("result");
        mock.ExpectsAscending(Expr.Body());
        foreach (var body in new[] { 1, 2, 2, 10 })
        {
            await Template.SendBodyAsync("direct:start", body);
        }

        await mock.AssertIsSatisfiedAsync();

        var descending = Mock("result");
        descending.Reset();
        descending.ExpectsDescending(e => e.Message.Body);
        await Template.SendBodyAsync("direct:start", 3);
        await Template.SendBodyAsync("direct:start", 4);
        var e = await Assert.ThrowsAsync<MockAssertionException>(() => descending.AssertIsSatisfiedAsync());
        Assert.Contains("not descending", e.Message);
    }

    [Fact]
    public async Task ExpectsAscending_fails_on_an_out_of_order_message()
    {
        var mock = Mock("result");
        mock.ExpectsAscending(Expr.Header("seq"));
        await Template.SendBodyAndHeaderAsync("direct:start", "a", "seq", 1);
        await Template.SendBodyAndHeaderAsync("direct:start", "b", "seq", 3);
        await Template.SendBodyAndHeaderAsync("direct:start", "c", "seq", 2);

        var e = await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
        Assert.Contains("message 1 has <3> and message 2 has <2>", e.Message);
    }

    [Fact]
    public async Task ExpectsNoDuplicates()
    {
        var mock = Mock("result");
        mock.ExpectsNoDuplicates(Expr.Header("id"));
        mock.ExpectsNoDuplicateBodies();
        await Template.SendBodyAndHeaderAsync("direct:start", "a", "id", 1);
        await Template.SendBodyAndHeaderAsync("direct:start", "b", "id", 2);
        await mock.AssertIsSatisfiedAsync();

        await Template.SendBodyAndHeaderAsync("direct:start", "c", "id", 1);
        var e = await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
        Assert.Contains("Duplicate message found on message 2 (same as message 0)", e.Message);
    }

    [Fact]
    public async Task ExpectedMessagesMatches_matches_each_message_with_its_predicate()
    {
        var mock = Mock("result");
        mock.ExpectedMessagesMatches(Expr.Body().IsEqualTo("a"), Expr.Body().StartsWith("b"));
        await Template.SendBodyAsync("direct:start", "a");
        await Template.SendBodyAsync("direct:start", "bee");
        await mock.AssertIsSatisfiedAsync();

        var other = Mock("result");
        other.Reset();
        other.ExpectedMessagesMatches(e => e.Message.GetBody<string>() == "x", e => e.Message.GetBody<string>() == "y");
        await Template.SendBodyAsync("direct:start", "y");
        await Template.SendBodyAsync("direct:start", "x");
        var error = await Assert.ThrowsAsync<MockAssertionException>(() => other.AssertIsSatisfiedAsync());
        Assert.Contains("Message 0: did not match predicate 0", error.Message);
    }

    [Fact]
    public async Task ExpectedPropertyValuesReceivedInAnyOrder()
    {
        await Context.AddRoutesAsync(RouteBuilder.Create(b =>
            b.From("direct:props").SetProperty("p", Expr.Body()).To("mock:props")));
        var mock = Mock("props");
        mock.ExpectedPropertyValuesReceivedInAnyOrder("p", 2, 1);
        await Template.SendBodyAsync("direct:props", 1);
        await Template.SendBodyAsync("direct:props", 2);
        await mock.AssertIsSatisfiedAsync();

        mock.Reset();
        mock.ExpectedPropertyValuesReceivedInAnyOrder("p", 1, 3);
        await Template.SendBodyAsync("direct:props", 1);
        await Template.SendBodyAsync("direct:props", 2);
        var e = await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
        Assert.Contains("Expected property p value <3> was not received", e.Message);
    }

    [Fact]
    public async Task Expects_runs_a_delayed_assertion()
    {
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);
        mock.Expects(() => Assert.Equal("ok", mock.ReceivedExchanges[0].Message.Body));
        await Template.SendBodyAsync("direct:start", "ok");
        await mock.AssertIsSatisfiedAsync();

        mock.Reset();
        mock.ExpectedMessageCount(1);
        mock.Expects(() => Assert.Equal("ok", mock.ReceivedExchanges[0].Message.Body));
        await Template.SendBodyAsync("direct:start", "not ok");
        var e = await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
        Assert.Contains("Assertion failed", e.Message);
    }

    [Fact]
    public async Task ReturnReplyBody_and_ReturnReplyHeader_combine()
    {
        var mock = Mock("result");
        mock.WhenAnyExchangeReceived(e => e.Message.SetHeader("seen", true));
        mock.ReturnReplyBody(Expr.Constant("reply"));
        mock.ReturnReplyHeader("replyHeader", e => e.Message.GetBody<string>() + "!");

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "in", ExchangePattern.InOut);

        Assert.Equal("reply", exchange.Message.Body);
        Assert.Equal("reply!", exchange.Message.GetHeader<string>("replyHeader"));
        Assert.True(exchange.Message.GetHeader<bool>("seen"));
    }

    [Fact]
    public async Task ExpectedExchangePattern()
    {
        var mock = Mock("result");
        mock.ExpectedExchangePattern(ExchangePattern.InOut);
        await Template.RequestBodyAsync("direct:start", "a");
        await mock.AssertIsSatisfiedAsync();

        await Template.SendBodyAsync("direct:start", "b");
        var e = await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
        Assert.Contains("Message 1 did not match exchange pattern InOut", e.Message);
    }

    [Fact]
    public async Task AssertExchangeReceived_returns_the_exchange_or_fails()
    {
        var mock = Mock("result");
        await Template.SendBodyAsync("direct:start", "a");
        await Template.SendBodyAsync("direct:start", "b");

        Assert.Equal("b", mock.AssertExchangeReceived(1).Message.Body);
        Assert.Equal(["a", "b"], mock.GetExchanges().Select(e => e.Message.Body));
        var e = Assert.Throws<MockAssertionException>(() => mock.AssertExchangeReceived(2));
        Assert.Contains("only 2 were received", e.Message);
    }

    [Fact]
    public async Task ResultMinimumWaitTime_fails_when_the_messages_arrive_too_soon()
    {
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);
        mock.ResultMinimumWaitTime = TimeSpan.FromSeconds(5);
        await Template.SendBodyAsync("direct:start", "early");

        var e = await Assert.ThrowsAsync<MockAssertionException>(() => mock.AssertIsSatisfiedAsync());
        Assert.Contains("Expected a minimum of 5000 ms waiting on the result", e.Message);
    }

    [Fact]
    public async Task ResultMinimumWaitTime_is_satisfied_when_the_messages_arrive_later()
    {
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);
        mock.ResultMinimumWaitTime = TimeSpan.FromMilliseconds(100);

        var assertion = mock.AssertIsSatisfiedAsync();
        await Task.Delay(300);
        await Template.SendBodyAsync("direct:start", "late");

        await assertion;
    }

    [Fact]
    public async Task AssertPeriod_detects_messages_arriving_after_the_expected_count_was_reached()
    {
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);
        mock.AssertPeriod = TimeSpan.FromSeconds(1);
        await Template.SendBodyAsync("direct:start", "one");

        var assertion = mock.AssertIsSatisfiedAsync();
        await Template.SendBodyAsync("direct:start", "two");

        var e = await Assert.ThrowsAsync<MockAssertionException>(() => assertion);
        Assert.Contains("Expected: <1> but was: <2>", e.Message);
    }
}
