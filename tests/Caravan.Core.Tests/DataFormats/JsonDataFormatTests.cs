using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Caravan.Builder;
using Caravan.DataFormat;

namespace Caravan.Core.Tests;

public class JsonDataFormatTests : ContextTestBase
{
    [Fact]
    public async Task Marshal_writes_json_bytes_and_sets_the_content_type()
    {
        await StartWithRoutes(b => b.From("direct:in").Marshal(DataFormats.Json()).To("mock:out"));
        var mock = Mock("mock:out").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:in", new Order("o1", 12.5m));

        await mock.AssertIsSatisfiedAsync();
        var message = mock.ReceivedExchanges[0].Message;
        Assert.IsType<byte[]>(message.Body);
        Assert.Equal("""{"id":"o1","total":12.5}""", message.GetBody<string>());
        Assert.Equal("application/json", message.GetHeader("Content-Type"));
    }

    [Fact]
    public async Task Typed_unmarshal_round_trips()
    {
        await StartWithRoutes(b => b.From("direct:in")
            .Marshal(DataFormats.Json())
            .Unmarshal(DataFormats.Json<Order>())
            .To("mock:out"));

        var result = await Template.RequestBodyAsync("direct:in", new Order("o2", 3m));

        Assert.Equal(new Order("o2", 3m), result);
    }

    [Fact]
    public async Task Untyped_unmarshal_gives_dictionaries_and_lists_usable_from_simple()
    {
        await StartWithRoutes(b => b.From("direct:in")
            .Unmarshal("json")
            .SetHeader("first", Expr.Simple("${body[items][0][name]}"))
            .To("mock:out"));
        var mock = Mock("mock:out").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:in", """{"id":7,"big":3000000000,"ratio":0.5,"ok":true,"none":null,"items":[{"name":"a"},{"name":"b"}]}""");

        await mock.AssertIsSatisfiedAsync();
        var message = mock.ReceivedExchanges[0].Message;
        var body = Assert.IsType<Dictionary<string, object?>>(message.Body);
        Assert.Equal(7, body["id"]);
        Assert.Equal(3000000000L, body["big"]);
        Assert.Equal(0.5, body["ratio"]);
        Assert.Equal(true, body["ok"]);
        Assert.Null(body["none"]);
        Assert.Equal(2, Assert.IsType<List<object?>>(body["items"]).Count);
        Assert.Equal("a", message.GetHeader("first"));
    }

    [Fact]
    public async Task Use_list_unmarshals_arrays_of_the_type()
    {
        await StartWithRoutes(b => b.From("direct:in").Unmarshal(DataFormats.Json<Order>(useList: true)));

        var result = await Template.RequestBodyAsync("direct:in", """[{"id":"a","total":1},{"id":"b","total":2}]""");

        Assert.Equal([new Order("a", 1), new Order("b", 2)], Assert.IsType<List<Order>>(result));
    }

    [Fact]
    public async Task Pretty_print_include_and_custom_options_apply()
    {
        var format = new JsonDataFormat { PrettyPrint = true, Include = "NON_NULL" };
        await StartWithRoutes(b => b.From("direct:in").Marshal(format).ConvertBodyTo(typeof(string)));

        var text = (string)(await Template.RequestBodyAsync("direct:in", new Order(null!, 2)))!;

        Assert.Contains("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\"", text, StringComparison.Ordinal);
        Assert.Contains("\"total\": 2", text, StringComparison.Ordinal);

        var pascal = new JsonDataFormat { SerializerOptions = new JsonSerializerOptions() };
        var exchange = new Exchange(Context);
        using var output = new MemoryStream();
        await pascal.MarshalAsync(exchange, new Order("x", 1), output);
        Assert.Equal("""{"Id":"x","Total":1}""", Encoding.UTF8.GetString(output.ToArray()));
        Assert.Throws<ArgumentException>(() => new JsonDataFormat { Include = "SOMETIMES" });
    }

    [Fact]
    public async Task Unmarshal_type_header_is_used_only_when_allowed()
    {
        var format = new JsonDataFormat { AllowUnmarshallType = true };
        await StartWithRoutes(b => b.From("direct:in").Unmarshal(format));

        var typed = await Template.RequestBodyAndHeaderAsync("direct:in", """{"id":"h","total":5}""", CaravanHeaders.JsonUnmarshalType, typeof(Order).FullName);
        var node = await Template.RequestBodyAndHeaderAsync("direct:in", """{"id":"h"}""", CaravanHeaders.JsonUnmarshalType, typeof(JsonNode));

        Assert.Equal(new Order("h", 5), typed);
        Assert.Equal("h", (string?)Assert.IsAssignableFrom<JsonNode>(node)["id"]);
    }

    [Fact]
    public async Task Content_type_header_can_be_disabled()
    {
        await StartWithRoutes(b => b.From("direct:in").Marshal(new JsonDataFormat { ContentTypeHeader = false }).To("mock:out"));
        var mock = Mock("mock:out").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:in", new Order("o", 1));

        await mock.AssertIsSatisfiedAsync();
        Assert.False(mock.ReceivedExchanges[0].Message.HasHeader("Content-Type"));
    }

    [Fact]
    public async Task Invalid_json_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:in").Unmarshal(DataFormats.Json<Order>()));

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:in", "{not json"));

        Assert.IsAssignableFrom<JsonException>(e.InnerException);
    }

    [Fact]
    public async Task Null_body_fails_unmarshal_unless_allowed()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:strict").Unmarshal("json");
            b.From("direct:lenient").Unmarshal("json", allowNullBody: true);
        });

        var e = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:strict", null));
        Assert.IsType<InvalidPayloadException>(e.InnerException);
        Assert.Null(await Template.RequestBodyAsync("direct:lenient", null));
    }
}
