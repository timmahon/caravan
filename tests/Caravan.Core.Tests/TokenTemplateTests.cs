using Caravan.Support;

namespace Caravan.Core.Tests;

public class TokenTemplateTests : ContextTestBase
{
    [Fact]
    public void Substitutes_supported_tokens()
    {
        var e = new Exchange(Context);
        e.Message.Body = "B";
        e.Message.SetHeader("h", 1);
        e.SetProperty("p", "P");
        e.CurrentRouteId = "r1";

        var text = TokenTemplate.Parse("${body}|${in.body}|${header.h}|${headers.H}|${header[h]}|${exchangeProperty.p}|${exchangeProperty['p']}|${routeId}|${header.none}|")
            .Evaluate(e);

        Assert.Equal("B|B|1|1|1|P|P|r1||", text);
        Assert.Equal(e.ExchangeId, TokenTemplate.Parse("${exchangeId}").Evaluate(e));
        Assert.Equal(e.Message.MessageId, TokenTemplate.Parse("${id}").Evaluate(e));
    }

    [Fact]
    public void Text_without_tokens_is_constant()
    {
        Assert.False(TokenTemplate.HasTokens("mock:a?x={{y}}"));
        Assert.Equal("mock:a?x={{y}}", TokenTemplate.Parse("mock:a?x={{y}}").Evaluate(new Exchange(Context)));
    }

    [Theory]
    [InlineData("${nope}")]
    [InlineData("${header.x")]
    [InlineData("${header.}")]
    public void Unknown_or_malformed_tokens_fail_at_parse_time(string template)
    {
        Assert.Throws<ArgumentException>(() => TokenTemplate.Parse(template));
    }
}
