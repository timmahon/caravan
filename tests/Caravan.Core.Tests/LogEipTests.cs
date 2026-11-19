using Caravan.Builder;
using Microsoft.Extensions.Logging;

namespace Caravan.Core.Tests;

public class LogEipTests : ContextTestBase
{
    private readonly CapturingLoggerProvider _provider = new();

    public override Task InitializeAsync()
    {
        Context.LoggerFactory = LoggerFactory.Create(b => b.AddProvider(_provider).SetMinimumLevel(LogLevel.Debug));
        return base.InitializeAsync();
    }

    [Fact]
    public async Task Logs_message_with_tokens_using_route_id_as_logger_name()
    {
        await StartWithRoutes(b => b.From("direct:start").RouteId("orders")
            .Log("Got ${body} from ${header.source}")
            .To("mock:result"));
        Mock("result").ExpectedBodiesReceived("order-1");

        await Template.SendBodyAndHeaderAsync("direct:start", "order-1", "source", "web");

        await Mock("result").AssertIsSatisfiedAsync();
        var line = Assert.Single(_provider.Messages);
        Assert.Equal(("orders", LogLevel.Information, "Got order-1 from web"), line);
    }

    [Fact]
    public async Task Level_and_logger_name_can_be_set()
    {
        await StartWithRoutes(b => b.From("direct:start")
            .Log(LogLevel.Warning, "warn ${exchangeProperty.p}")
            .Log(LogLevel.Debug, "audit", "debug ${body}")
            .Log(LogLevel.Trace, "audit", "not enabled"));

        await Template.SendBodyAndPropertyAsync("direct:start", "b", "p", 1);

        Assert.Equal(2, _provider.Messages.Count);
        Assert.Equal(LogLevel.Warning, _provider.Messages[0].Level);
        Assert.Equal("warn 1", _provider.Messages[0].Message);
        Assert.Equal(("audit", LogLevel.Debug, "debug b"), _provider.Messages[1]);
    }

    [Fact]
    public async Task Unknown_token_fails_when_the_route_is_created()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:start").Log("${bogus}")));

        await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());
    }
}
