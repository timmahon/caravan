using System.Text;
using Caravan.Builder;
using Caravan.TypeConversion;

namespace Caravan.Core.Tests;

public class ConvertBodyTests : ContextTestBase
{
    [Fact]
    public async Task Converts_body_to_type()
    {
        await StartWithRoutes(b => b.From("direct:start").ConvertBodyTo(typeof(int)).To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "123");

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(123, mock.ReceivedBodies[0]);
    }

    [Fact]
    public async Task Null_body_and_assignable_body_are_left_alone()
    {
        var list = new List<int> { 1 };
        await StartWithRoutes(b =>
        {
            b.From("direct:null").ConvertBodyTo(typeof(int)).To("mock:null");
            b.From("direct:same").ConvertBodyTo(typeof(IEnumerable<int>)).To("mock:same");
        });
        Mock("null").ExpectedMessageCount(1);
        Mock("same").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:null", null);
        await Template.SendBodyAsync("direct:same", list);

        await Mock("null").AssertIsSatisfiedAsync();
        await Mock("same").AssertIsSatisfiedAsync();
        Assert.Null(Mock("null").ReceivedBodies[0]);
        Assert.Same(list, Mock("same").ReceivedBodies[0]);
    }

    [Fact]
    public async Task Failure_throws_invalid_payload_caused_by_no_type_conversion()
    {
        await StartWithRoutes(b => b.From("direct:start").ConvertBodyTo(typeof(Guid)).To("mock:result"));
        Mock("result").ExpectedMessageCount(0);

        var result = await Template.SendAsync("direct:start", e => e.Message.Body = "not a guid");

        var payload = Assert.IsType<InvalidPayloadException>(result.Exception);
        Assert.Equal(typeof(Guid), payload.Type);
        Assert.NotNull(result.GetException<NoTypeConversionAvailableException>());
        await Mock("result").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Non_mandatory_failure_sets_null_body()
    {
        await StartWithRoutes(b => b.From("direct:start").ConvertBodyTo(typeof(Guid), mandatory: false).To("mock:result"));
        var mock = Mock("result");
        mock.ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "not a guid");

        await mock.AssertIsSatisfiedAsync();
        Assert.Null(mock.ReceivedBodies[0]);
    }

    [Fact]
    public async Task Charset_is_used_between_text_and_bytes()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:encode").ConvertBodyTo(typeof(byte[]), "UTF-16").To("mock:bytes");
            b.From("direct:decode").ConvertBodyTo(typeof(string), "utf-16").To("mock:text");
        });
        Mock("bytes").ExpectedMessageCount(1);
        Mock("text").ExpectedBodiesReceived("héllo");

        await Template.SendBodyAsync("direct:encode", "héllo");
        await Template.SendBodyAsync("direct:decode", Encoding.Unicode.GetBytes("héllo"));

        await Mock("bytes").AssertIsSatisfiedAsync();
        await Mock("text").AssertIsSatisfiedAsync();
        Assert.Equal(Encoding.Unicode.GetBytes("héllo"), Mock("bytes").ReceivedBodies[0]);
    }
}
