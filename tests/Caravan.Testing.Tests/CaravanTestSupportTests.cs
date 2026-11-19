using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Spi;

namespace Caravan.Testing.Tests;

/// <summary>The test support base class, used the way a test class would (mirrors Camel's CamelTestSupport tests).</summary>
public class CaravanTestSupportTests
{
    [Fact]
    public async Task Sets_up_starts_and_disposes_the_context()
    {
        var test = new OrderRouteTest();
        await test.SetUpAsync();

        Assert.True(test.Context.IsStarted);
        test.GetMockEndpoint("result").ExpectedBodiesReceived("ORDER");
        await test.Template.SendBodyAsync("direct:orders", "order");
        await test.AssertMockEndpointsSatisfiedAsync();
        Assert.Equal("approved", test.Context.Registry.Lookup<string>("status"));

        test.ResetMocks();
        Assert.Equal(0, test.GetMockEndpoint("mock:result").ReceivedCounter);

        await test.DisposeAsync();
        await test.DisposeAsync();
        Assert.Equal(ServiceStatus.Stopped, test.Context.Status);
    }

    [Fact]
    public async Task Members_require_set_up()
    {
        await using var test = new OrderRouteTest();

        Assert.Throws<InvalidOperationException>(() => test.Context);
        Assert.Throws<InvalidOperationException>(() => test.Template);
        Assert.Throws<InvalidOperationException>(() => test.ConsumerTemplate);
    }

    [Fact]
    public async Task GetMockEndpoint_without_create_fails_for_unknown_endpoints()
    {
        await using var test = new OrderRouteTest();
        await test.SetUpAsync();

        Assert.Same(test.GetMockEndpoint("mock:result"), test.GetMockEndpoint("mock:result", create: false));
        Assert.Throws<NoSuchEndpointException>(() => test.GetMockEndpoint("mock:unknown", create: false));
    }

    [Fact]
    public async Task UseAdviceWith_defers_the_start_until_advised()
    {
        await using var test = new AdvisedTest();
        await test.SetUpAsync();
        Assert.False(test.Context.IsStarted);

        await test.AdviceWithAsync("poller", a =>
        {
            a.ReplaceFromWith("direct:start");
            a.WeaveByToUri("direct:remote").Replace(r => r.To("mock:remote"));
        });
        await test.StartAsync();
        await test.Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, test.GetMockEndpoint("mock:remote").ReceivedCounter);
    }

    [Fact]
    public async Task IsMockEndpoints_mocks_matching_endpoints()
    {
        await using var test = new MockingTest();
        await test.SetUpAsync();

        await test.Template.SendBodyAsync("direct:start", "x");

        Assert.Equal(1, test.GetMockEndpoint("mock:direct:audit").ReceivedCounter);
        Assert.Equal(1, test.GetMockEndpoint("mock:audit-called").ReceivedCounter);
        Assert.Equal(1, test.GetMockEndpoint("mock:direct:remote").ReceivedCounter);
        Assert.Equal(0, test.GetMockEndpoint("mock:remote-called").ReceivedCounter);
        Assert.Equal(1, test.GetMockEndpoint("mock:result").ReceivedCounter);
    }

    [Fact]
    public async Task ConsumerTemplate_receives_from_endpoints()
    {
        await using var test = new OrderRouteTest();
        await test.SetUpAsync();

        await test.Template.SendBodyAsync("seda:outbox", "mail");

        Assert.Equal("mail", await test.ConsumerTemplate.ReceiveBodyAsync("seda:outbox", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task UseFakeTimeProvider_drives_timers_with_the_fake_clock()
    {
        await using var test = new TimerTest();
        await test.SetUpAsync();
        Assert.Same(test.FakeTimeProvider, test.Context.TimeProvider);
        using var notify = new NotifyBuilder(test.Context).WhenDone(3).Create();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!notify.Matches() && DateTime.UtcNow < deadline)
        {
            test.FakeTimeProvider!.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(1);
        }

        Assert.True(notify.Matches());
        Assert.True(test.GetMockEndpoint("mock:tick").ReceivedCounter >= 3);
    }

    private sealed class OrderRouteTest : CaravanTestSupport
    {
        protected override void BindToRegistry(IRegistry registry)
        {
            registry.Bind("status", "approved");
            registry.Bind("upper", DelegateProcessor.From(e => e.Message.Body = e.Message.GetBody<string>()!.ToUpperInvariant()));
        }

        protected override IRoutesBuilder CreateRouteBuilder() =>
            RouteBuilder.Create(b => b.From("direct:orders").ProcessRef("upper").To("mock:result"));
    }

    private sealed class AdvisedTest : CaravanTestSupport
    {
        protected override bool UseAdviceWith => true;

        protected override IEnumerable<IRoutesBuilder> CreateRouteBuilders() =>
        [
            RouteBuilder.Create(b => b.From("timer:poll?period=3600000").RouteId("poller").To("direct:remote")),
            RouteBuilder.Create(b => b.From("direct:remote").To("mock:remote-called")),
        ];
    }

    private sealed class MockingTest : CaravanTestSupport
    {
        protected override string? IsMockEndpoints => "direct:audit";

        protected override string? IsMockEndpointsAndSkip => "direct:remote";

        protected override IRoutesBuilder CreateRouteBuilder() => RouteBuilder.Create(b =>
        {
            b.From("direct:start").To("direct:audit").To("direct:remote").To("mock:result");
            b.From("direct:audit").To("mock:audit-called");
            b.From("direct:remote").To("mock:remote-called");
        });
    }

    private sealed class TimerTest : CaravanTestSupport
    {
        protected override bool UseFakeTimeProvider => true;

        protected override IRoutesBuilder CreateRouteBuilder() =>
            RouteBuilder.Create(b => b.From("timer:tick?period=60000&delay=60000").To("mock:tick"));
    }
}
