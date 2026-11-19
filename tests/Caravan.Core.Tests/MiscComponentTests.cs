using Caravan.Builder;
using Caravan.Components.Browse;
using Caravan.Components.Ref;
using Caravan.Components.Seda;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Core.Tests;

public class MiscComponentTests : ContextTestBase
{
    [Fact]
    public async Task Stub_accepts_any_uri_and_options_and_behaves_like_seda()
    {
        await StartWithRoutes(b => b.From("stub:jms:queue:orders?concurrentConsumers=2&jmsOption=whatever").To("mock:stub"));
        var mock = Mock("mock:stub").ExpectedBodiesReceivedInAnyOrder("a", "b");

        await Template.SendBodyAsync("stub:jms:queue:orders?anything=else", "a");
        await Template.SendBodyAsync("stub:jms:queue:orders", "b");

        await mock.AssertIsSatisfiedAsync();
        var endpoint = Context.GetEndpoint<SedaEndpoint>("stub:jms:queue:orders?concurrentConsumers=2&jmsOption=whatever");
        Assert.Equal("jms:queue:orders", endpoint.Name);
        Assert.Equal(2, endpoint.ConcurrentConsumers);
        Assert.True(endpoint.IsLenientProperties);
        Assert.Equal(0, Context.GetEndpoint<SedaEndpoint>("seda:jms:queue:orders").ConsumerCount);
    }

    [Fact]
    public async Task Stub_supports_request_reply()
    {
        await StartWithRoutes(b => b.From("stub:http://remote/api").Transform(Expr.Simple("reply to ${body}")));

        Assert.Equal("reply to ping", await Template.RequestBodyAsync("stub:http://remote/api", "ping"));
    }

    [Fact]
    public async Task Ref_resolves_endpoints_from_the_registry()
    {
        Context.Registry.Bind("target", Context.GetEndpoint("mock:target"));
        Context.Registry.Bind("byUri", "mock:byUri");
        await StartWithRoutes(b => b.From("direct:in").To("ref:target").To("ref:byUri"));
        Mock("mock:target").ExpectedBodiesReceived("x");
        Mock("mock:byUri").ExpectedBodiesReceived("x");

        await Template.SendBodyAsync("direct:in", "x");

        await Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
        Assert.Same(Context.GetEndpoint("mock:target"), Context.GetEndpoint("ref:target"));
        Assert.Throws<NoSuchBeanException>(() => Context.GetEndpoint("ref:missing"));
    }

    [Fact]
    public async Task Ref_wraps_endpoints_that_are_not_endpoint_bases()
    {
        var inner = Context.GetEndpoint("direct:wrapped");
        Context.Registry.Bind("wrapped", new ForwardingEndpoint(inner));
        await StartWithRoutes(b =>
        {
            b.From("ref:wrapped").To("mock:wrapped");
            b.From("direct:in").To("ref:wrapped");
        });
        var mock = Mock("mock:wrapped").ExpectedBodiesReceived("hi");

        await Template.SendBodyAsync("direct:in", "hi");

        await mock.AssertIsSatisfiedAsync();
        Assert.Same(inner, Context.GetEndpoint<RefEndpoint>("ref:wrapped").Target is ForwardingEndpoint f ? f.Inner : null);
    }

    [Fact]
    public async Task Browse_records_exchanges_and_passes_them_on()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:in").To("browse:audit?browseLimit=2").To("mock:after");
            b.From("browse:audit?browseLimit=2").SetHeader("seen", Expr.Constant(true));
        });
        var mock = Mock("mock:after").ExpectedMessageCount(3);
        mock.AllMessages().Header("seen").IsEqualTo(true);

        foreach (var body in new[] { "a", "b", "c" })
        {
            await Template.SendBodyAsync("direct:in", body);
        }

        await mock.AssertIsSatisfiedAsync();
        var browse = Context.GetEndpoint<BrowseEndpoint>("browse:audit?browseLimit=2");
        Assert.Equal(["b", "c"], browse.Exchanges.Select(e => e.Message.Body));
        browse.Clear();
        Assert.Empty(browse.Exchanges);
    }

    [Fact]
    public async Task Browse_without_consumer_only_records()
    {
        await Context.StartAsync();

        await Template.SendBodyAsync("browse:only", "x");

        Assert.Equal("x", Assert.Single(Context.GetEndpoint<BrowseEndpoint>("browse:only").Exchanges).Message.Body);
    }

    private sealed class ForwardingEndpoint(IEndpoint inner) : ServiceSupport, IEndpoint
    {
        public IEndpoint Inner { get; } = inner;

        public string EndpointUri => "forward:" + Inner.EndpointUri;

        public string EndpointKey => EndpointUri;

        public ICaravanContext Context => Inner.Context;

        public IComponent? Component => null;

        public bool IsSingleton => true;

        public bool IsLenientProperties => false;

        public IProducer CreateProducer() => Inner.CreateProducer();

        public IConsumer CreateConsumer(IProcessor processor) => Inner.CreateConsumer(processor);

        public Exchange CreateExchange(ExchangePattern? pattern = null) => Inner.CreateExchange(pattern);
    }
}
