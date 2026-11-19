using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Impl;
using Caravan.Processors;

namespace Caravan.Core.Tests;

public class ToDynamicTests : ContextTestBase
{
    [Fact]
    public async Task Uri_template_uses_headers_body_and_properties()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .SetProperty("suffix", Expr.Constant("1"))
            .ToD("mock:${header.target}-${exchangeProperty.suffix}"));
        Mock("a-1").ExpectedBodiesReceived("x");
        Mock("b-1").ExpectedBodiesReceived("y");

        await Template.SendBodyAndHeaderAsync("direct:start", "x", "target", "a");
        await Template.SendBodyAndHeaderAsync("direct:start", "y", "target", "b");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Contains("a-1", (string)Mock("a-1").ReceivedExchanges[0].GetProperty(ExchangePropertyKeys.ToEndpoint)!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uri_from_function_and_expression()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:func").ToD(e => "mock:" + e.Message.Body);
            b.From("direct:expr").ToD(Expr.Constant("mock:").Append(Expr.Header("to")));
        });
        Mock("f").ExpectedMessageCount(1);
        Mock("e").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:func", "f");
        await Template.SendBodyAndHeaderAsync("direct:expr", "x", "to", "e");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Body_token_and_request_reply_through_direct()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").ToD("direct:${body}");
            b.From("direct:upper").SetBody(_ => "UPPER");
        });

        Assert.Equal("UPPER", await Template.RequestBodyAsync<string>("direct:start", "upper"));
    }

    [Fact]
    public async Task Producers_are_cached_up_to_cache_size()
    {
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").ToD("mock:${body}", cacheSize: 2));

        foreach (var body in new[] { "a", "b", "a", "c", "d" })
        {
            await Template.SendBodyAsync("direct:start", body);
        }

        var processor = FindProcessor<SendDynamicProcessor>("r");
        Assert.Equal(2, processor.CachedCount);
        Assert.Equal(2, Mock("a").ReceivedCounter);
        Assert.Equal(1, Mock("d").ReceivedCounter);
    }

    [Fact]
    public async Task Cache_can_be_disabled()
    {
        await StartWithRoutes(b => b.From("direct:start").RouteId("r").ToD("mock:${body}", cacheSize: -1));
        Mock("a").ExpectedMessageCount(2);

        await Template.SendBodyAsync("direct:start", "a");
        await Template.SendBodyAsync("direct:start", "a");

        await Mock("a").AssertIsSatisfiedAsync();
        Assert.Equal(0, FindProcessor<SendDynamicProcessor>("r").CachedCount);
    }

    [Fact]
    public async Task Invalid_endpoint_fails_unless_ignored()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:strict").ToD("${header.uri}").To("mock:after-strict");
            b.From("direct:lenient").ToD("${header.uri}", ignoreInvalidEndpoint: true).To("mock:after-lenient");
        });
        Mock("after-strict").ExpectedMessageCount(0);
        Mock("after-lenient").ExpectedMessageCount(2);

        var ex = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("direct:strict", "x", "uri", "nosuchscheme:foo"));
        Assert.IsType<NoSuchEndpointException>(ex.InnerException);
        await Template.SendBodyAndHeaderAsync("direct:lenient", "x", "uri", "nosuchscheme:foo");
        await Template.SendBodyAsync("direct:lenient", "no uri header");

        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Pattern_is_applied_while_sending()
    {
        await StartWithRoutes(b => b.From("direct:start").ToD("mock:${header.t}", ExchangePattern.InOut));
        var mock = Mock("x");
        mock.ExpectedMessageCount(1);

        await Template.SendBodyAndHeaderAsync("direct:start", "b", "t", "x");

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(ExchangePattern.InOut, mock.ReceivedExchanges[0].Pattern);
    }

    private T FindProcessor<T>(string routeId)
        where T : class =>
        ((Route)Context.GetRoute(routeId)!).RouteContext.Services.OfType<T>().Single();
}
