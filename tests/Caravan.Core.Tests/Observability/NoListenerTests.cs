using Caravan.Builder;
using Caravan.Observability;

namespace Caravan.Core.Tests.Observability;

/// <summary>Without listeners nothing is recorded: no headers, no spans (the zero-cost path).</summary>
public class NoListenerTests : ContextTestBase
{
    [Fact]
    public async Task Without_an_activity_no_trace_headers_are_added()
    {
        Context.Name = "unobserved-" + Guid.NewGuid().ToString("N");
        var mock = Mock("mock:a").ExpectedMessageCount(1);
        await StartWithRoutes(b => b.From("direct:a").To("seda:b"));
        Context.AddRoutes(RouteBuilder.Create(b => b.From("seda:b").To("mock:a")));

        await Template.SendBodyAsync("direct:a", "x");
        await mock.AssertIsSatisfiedAsync();

        var headers = mock.GetExchanges()[0].Message.Headers;
        Assert.DoesNotContain(CaravanSemanticConventions.TraceParentHeader, headers.Keys);
        Assert.DoesNotContain(CaravanSemanticConventions.TraceStateHeader, headers.Keys);
    }

    [Fact]
    public void Uri_sanitizer_masks_sensitive_options_and_user_info_passwords()
    {
        Assert.Equal("kafka:topic?brokers=b&password=xxxxxx&sasl.jaas.secret=xxxxxx", UriSanitizer.Sanitize("kafka:topic?brokers=b&password=p&sasl.jaas.secret=s"));
        Assert.Equal("ftp://bob:xxxxxx@host/dir?binary=true", UriSanitizer.Sanitize("ftp://bob:pw@host/dir?binary=true"));
        Assert.Equal("aws2-s3:b?accessKey=xxxxxx&secretKey=xxxxxx&region=eu", UriSanitizer.Sanitize("aws2-s3:b?accessKey=A&secretKey=S&region=eu"));
        Assert.Equal("direct:a", UriSanitizer.Sanitize("direct:a"));
        Assert.Equal("http://host/path", UriSanitizer.WithoutQuery("http://host/path?token=t"));
        Assert.Null(UriSanitizer.Sanitize(null));
    }
}
