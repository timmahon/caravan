namespace Caravan.Dsl.Yaml.Tests;

public class SmokeTests : YamlTestBase
{
    [Fact]
    public async Task Camel_route_with_choice_runs()
    {
        await StartAsync("""
            - route:
                id: orders
                from:
                  uri: direct:orders
                  steps:
                    - setHeader: { name: received, constant: "true" }
                    - choice:
                        when:
                          - simple: "${header.total} > 100"
                            steps: [ { to: mock:big } ]
                        otherwise:
                          steps: [ { to: mock:small } ]
                    - to: { uri: log:orders, parameters: { showHeaders: true } }
                    - to: mock:all
            """);
        Mock("big").ExpectedBodiesReceived("b");
        Mock("small").ExpectedBodiesReceived("s");
        Mock("all").ExpectedMessageCount(2);

        await Template.SendBodyAndHeaderAsync("direct:orders", "b", "total", 500);
        await Template.SendBodyAndHeaderAsync("direct:orders", "s", "total", 5);

        await Mock("big").AssertIsSatisfiedAsync();
        await Mock("small").AssertIsSatisfiedAsync();
        await Mock("all").AssertIsSatisfiedAsync();
        Assert.Equal("true", Mock("all").ReceivedExchanges[0].Message.GetHeader("received"));
    }
}
