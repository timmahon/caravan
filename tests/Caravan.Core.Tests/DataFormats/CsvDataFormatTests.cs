using Caravan.Builder;
using Caravan.DataFormat;

namespace Caravan.Core.Tests;

public class CsvDataFormatTests : ContextTestBase
{
    [Fact]
    public async Task Unmarshal_gives_lists_with_rfc4180_quoting()
    {
        await StartWithRoutes(b => b.From("direct:in").Unmarshal("csv"));

        var result = await Template.RequestBodyAsync("direct:in", "a,b,c\r\n1,\"x, y\",\"say \"\"hi\"\"\"\n\n2,\"multi\nline\",\r\n");

        var rows = Assert.IsType<List<List<string>>>(result);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "x, y", "say \"hi\""], rows[1]);
        Assert.Equal(["2", "multi\nline", ""], rows[2]);
    }

    [Fact]
    public async Task Use_maps_keys_records_by_the_header_line_or_the_configured_header()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:maps").Unmarshal(DataFormats.Csv(useMaps: true));
            b.From("direct:header").Unmarshal(new CsvDataFormat { UseMaps = true, Header = "id, name" });
            b.From("direct:replace").Unmarshal(new CsvDataFormat { UseMaps = true, Header = "x,y", SkipHeaderRecord = true });
        });

        var maps = Assert.IsType<List<Dictionary<string, string?>>>(await Template.RequestBodyAsync("direct:maps", "id,name\n1,Ann\n2,Bob\n"));
        Assert.Equal("Bob", maps[1]["name"]);
        Assert.Equal(2, maps.Count);

        var configured = Assert.IsType<List<Dictionary<string, string?>>>(await Template.RequestBodyAsync("direct:header", "1,Ann\n2\n"));
        Assert.Equal("Ann", configured[0]["name"]);
        Assert.Null(configured[1]["name"]);

        var replaced = Assert.IsType<List<Dictionary<string, string?>>>(await Template.RequestBodyAsync("direct:replace", "id,name\n1,Ann\n"));
        Assert.Equal("Ann", Assert.Single(replaced)["y"]);
    }

    [Fact]
    public async Task Options_delimiter_skip_header_trim_escape_and_empty_lines()
    {
        var format = new CsvDataFormat { Delimiter = ';', SkipHeaderRecord = true, Trim = true, Escape = '\\', IgnoreEmptyLines = false };
        await StartWithRoutes(b => b.From("direct:in").Unmarshal(format));

        var rows = Assert.IsType<List<List<string>>>(await Template.RequestBodyAsync("direct:in", "h1;h2\n a ; b\\;c \n\n\" q \";x"));

        Assert.Equal(["a", "b;c"], rows[0]);
        Assert.Equal([""], rows[1]);
        Assert.Equal([" q ", "x"], rows[2]);
    }

    [Fact]
    public async Task Marshal_writes_lists_maps_and_quotes_when_needed()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:lists").Marshal("csv").ConvertBodyTo(typeof(string));
            b.From("direct:maps").Marshal(new CsvDataFormat { Header = "name,id" }).ConvertBodyTo(typeof(string));
            b.From("direct:noheader").Marshal(new CsvDataFormat { Header = "name,id", SkipHeaderRecord = true, QuoteAll = true, RecordSeparator = "\n" })
                .ConvertBodyTo(typeof(string));
        });

        var lists = new List<List<object?>> { new() { 1, "a,b", null }, new() { 2, "say \"x\"", "line\nbreak" } };
        Assert.Equal("1,\"a,b\",\r\n2,\"say \"\"x\"\"\",\"line\nbreak\"\r\n", await Template.RequestBodyAsync("direct:lists", lists));
        Assert.Equal("x,y\r\n", await Template.RequestBodyAsync("direct:lists", new List<string> { "x", "y" }));

        var maps = new List<Dictionary<string, object?>> { new() { ["id"] = 1, ["name"] = "Ann" }, new() { ["id"] = 2 } };
        Assert.Equal("name,id\r\nAnn,1\r\n,2\r\n", await Template.RequestBodyAsync("direct:maps", maps));
        Assert.Equal("\"Ann\",\"1\"\n", await Template.RequestBodyAsync("direct:noheader", new Dictionary<string, object?> { ["id"] = 1, ["name"] = "Ann" }));
    }

    [Fact]
    public async Task Round_trip_through_csv()
    {
        await StartWithRoutes(b => b.From("direct:in").Marshal("csv").Unmarshal("csv"));
        var rows = new List<List<string>> { new() { "a", "b\"c" }, new() { "d,e", "f" } };

        Assert.Equal(rows, await Template.RequestBodyAsync("direct:in", rows));
    }

    [Fact]
    public void Unclosed_quote_is_an_error()
    {
        Assert.Throws<FormatException>(() => new CsvDataFormat().Parse("a,\"b"));
    }
}
