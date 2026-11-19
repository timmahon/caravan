using System.IO.Compression;
using System.Text;
using Caravan.Builder;
using Caravan.DataFormat;

namespace Caravan.Core.Tests;

public class BinaryDataFormatTests : ContextTestBase
{
    [Fact]
    public async Task Base64_chunks_lines_like_commons_codec_and_round_trips()
    {
        var data = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        await StartWithRoutes(b =>
        {
            b.From("direct:encode").Marshal("base64").ConvertBodyTo(typeof(string));
            b.From("direct:roundtrip").Marshal(DataFormats.Base64()).Unmarshal(DataFormats.Base64());
        });

        var encoded = (string)(await Template.RequestBodyAsync("direct:encode", data))!;
        var lines = encoded.Split("\r\n");
        Assert.Equal(76, lines[0].Length);
        Assert.Equal(string.Empty, lines[^1]);
        Assert.Equal(Convert.ToBase64String(data), string.Concat(lines));
        Assert.Equal(data, await Template.RequestBodyAsync("direct:roundtrip", data));
        Assert.Equal("aGk=\r\n", await Template.RequestBodyAsync("direct:encode", "hi"));
    }

    [Fact]
    public async Task Base64_url_safe_and_single_line()
    {
        var data = new byte[] { 0xfb, 0xff, 0xfe };
        var format = DataFormats.Base64(lineLength: 0, urlSafe: true);
        await StartWithRoutes(b => b.From("direct:in").Marshal(format).ConvertBodyTo(typeof(string)));

        Assert.Equal("-__-", await Template.RequestBodyAsync("direct:in", data));
        Assert.Equal(data, Base64DataFormat.Decode("-__-"));
        Assert.Equal(Encoding.UTF8.GetBytes("hello"), Base64DataFormat.Decode(" aGVs\r\nbG8 "));
        Assert.Throws<FormatException>(() => Base64DataFormat.Decode("***"));
    }

    [Fact]
    public async Task Gzip_and_zip_deflater_compress_in_standard_formats()
    {
        var text = string.Concat(Enumerable.Repeat("compress me ", 100));
        await StartWithRoutes(b =>
        {
            b.From("direct:gzip").Marshal("gzipDeflater");
            b.From("direct:zip").Marshal(DataFormats.ZipDeflater(compressionLevel: 9));
            b.From("direct:gunzip").Unmarshal("gzip").ConvertBodyTo(typeof(string));
            b.From("direct:unzip").Unmarshal("zipDeflater").ConvertBodyTo(typeof(string));
        });

        var gzipped = (byte[])(await Template.RequestBodyAsync("direct:gzip", text))!;
        var zipped = (byte[])(await Template.RequestBodyAsync("direct:zip", text))!;
        Assert.True(gzipped.Length < text.Length);
        Assert.Equal(new byte[] { 0x1f, 0x8b }, gzipped[..2]);
        Assert.Equal(0x78, zipped[0]);
        Assert.Equal(text, await new StreamReader(new GZipStream(new MemoryStream(gzipped), CompressionMode.Decompress)).ReadToEndAsync());
        Assert.Equal(text, await Template.RequestBodyAsync("direct:gunzip", gzipped));
        Assert.Equal(text, await Template.RequestBodyAsync("direct:unzip", zipped));
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.RequestBodyAsync("direct:gunzip", "not gzip"));
    }

    [Fact]
    public async Task String_data_format_uses_its_charset()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:marshal").Marshal(DataFormats.String("ISO-8859-1"));
            b.From("direct:unmarshal").Unmarshal(new StringDataFormat { Charset = "UTF-16" });
        });

        Assert.Equal(new byte[] { 0xe9 }, await Template.RequestBodyAsync("direct:marshal", "é"));
        Assert.Equal("é", await Template.RequestBodyAsync("direct:unmarshal", Encoding.Unicode.GetBytes("é")));
    }
}
