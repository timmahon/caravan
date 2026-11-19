using Caravan.Builder;
using Caravan.Components.Languages;

namespace Caravan.Core.Tests;

/// <summary>The <c>language</c> component (Camel's LanguageTest).</summary>
public class LanguageComponentTests : ContextTestBase
{
    [Fact]
    public async Task Evaluates_the_script_and_sets_the_result_as_body()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:simple").To("language:simple:Hello ${body} from ${header.who}").To("mock:simple");
            b.From("direct:header").To("language:header:who").To("mock:header");
            b.From("direct:noTransform").To("language:simple:${body}x?transform=false").To("mock:noTransform");
            b.From("direct:body").To("language:simple").To("mock:body");
        });

        await Template.SendBodyAndHeaderAsync("direct:simple", "World", "who", "Caravan");
        await Template.SendBodyAndHeaderAsync("direct:header", "x", "who", "me");
        await Template.SendBodyAsync("direct:noTransform", "same");
        await Template.SendBodyAndHeaderAsync("direct:body", "${header.n}-${header.n}", "n", 7);

        Assert.Equal(["Hello World from Caravan"], Mock("simple").ReceivedBodies);
        Assert.Equal(["me"], Mock("header").ReceivedBodies);
        Assert.Equal(["same"], Mock("noTransform").ReceivedBodies);
        Assert.Equal(["7-7"], Mock("body").ReceivedBodies);
    }

    [Fact]
    public void Endpoints_need_a_language_and_cannot_be_consumed()
    {
        var endpoint = Context.GetEndpoint<LanguageEndpoint>("language:constant:abc?cacheScript=false");
        Assert.Equal("constant", endpoint.LanguageName);
        Assert.Equal("abc", endpoint.Script);
        Assert.False(endpoint.CacheScript);
        Assert.Throws<NotSupportedException>(() => endpoint.CreateConsumer(new Spi.DelegateProcessor(_ => ValueTask.CompletedTask)));
        Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("language:"));
    }

    [Fact]
    public async Task Unknown_languages_fail_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").To("language:nope:x"));

        var exchange = await Template.SendAsync("direct:start", _ => { });

        Assert.IsType<NoSuchLanguageException>(exchange.Exception);
    }
}
