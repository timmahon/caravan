using System.Xml;
using System.Xml.Linq;
using Caravan.Builder;
using Caravan.DataFormat;

namespace Caravan.Core.Tests;

public class XmlDataFormatTests : ContextTestBase
{
    [Fact]
    public async Task Typed_round_trip_with_xml_serializer()
    {
        await StartWithRoutes(b => b.From("direct:in")
            .Marshal(DataFormats.Xml())
            .To("mock:xml")
            .Unmarshal(DataFormats.Xml<Invoice>()));
        var mock = Mock("mock:xml").ExpectedMessageCount(1);

        var result = await Template.RequestBodyAsync("direct:in", new Invoice { Number = "INV-1", Amount = 9.5m });

        var invoice = Assert.IsType<Invoice>(result);
        Assert.Equal("INV-1", invoice.Number);
        Assert.Equal(9.5m, invoice.Amount);
        await mock.AssertIsSatisfiedAsync();
        var xml = mock.ReceivedExchanges[0].Message.GetBody<string>()!;
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml, StringComparison.Ordinal);
        Assert.Contains("\n  <Number>INV-1</Number>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("xmlns:xsi", xml, StringComparison.Ordinal);
        Assert.Equal("application/xml", mock.ReceivedExchanges[0].Message.GetHeader("Content-Type"));
    }

    [Fact]
    public async Task Untyped_unmarshal_loads_an_xdocument()
    {
        await StartWithRoutes(b => b.From("direct:in").Unmarshal("xml"));

        var result = await Template.RequestBodyAsync("direct:in", "<order id=\"5\"><item>a</item></order>");

        var document = Assert.IsType<XDocument>(result);
        Assert.Equal("5", document.Root!.Attribute("id")!.Value);
    }

    [Fact]
    public async Task Fragment_and_compact_output_and_xml_bodies_are_written_as_is()
    {
        var format = new XmlDataFormat { PrettyPrint = false, Fragment = true };
        await StartWithRoutes(b => b.From("direct:in").Marshal(format).ConvertBodyTo(typeof(string)));

        Assert.Equal("<Invoice><Number>2</Number><Amount>0</Amount></Invoice>", await Template.RequestBodyAsync("direct:in", new Invoice { Number = "2" }));
        Assert.Equal("<a><b /></a>", await Template.RequestBodyAsync("direct:in", XElement.Parse("<a><b/></a>")));
        Assert.Equal("<raw/>", await Template.RequestBodyAsync("direct:in", "<raw/>"));
    }

    [Fact]
    public async Task Dtds_are_rejected_and_malformed_xml_fails()
    {
        await StartWithRoutes(b => b.From("direct:in").Unmarshal(DataFormats.Xml()));

        var dtd = await Assert.ThrowsAsync<CaravanExecutionException>(() =>
            Template.RequestBodyAsync("direct:in", "<!DOCTYPE x [<!ENTITY e \"boom\">]><x>&e;</x>"));
        Assert.IsType<XmlException>(dtd.InnerException);
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:in", "<open>"));
    }

    public class Invoice
    {
        public string? Number { get; set; }

        public decimal Amount { get; set; }
    }
}
