using Caravan.Builder;
using Caravan.Language;
using Caravan.Language.Simple;

namespace Caravan.Core.Tests;

public class FileLanguageTests : FileTestBase
{
    private Exchange FileExchange(string name = "sub/report.tar.gz")
    {
        var exchange = new Exchange(Context);
        var message = exchange.Message;
        message.SetHeader(CaravanHeaders.FileName, name);
        message.SetHeader(CaravanHeaders.FileNameOnly, Path.GetFileName(name));
        message.SetHeader(CaravanHeaders.FileParent, "inbox/sub");
        message.SetHeader(CaravanHeaders.FilePath, "inbox/" + name);
        message.SetHeader(CaravanHeaders.FileAbsolute, false);
        message.SetHeader(CaravanHeaders.FileAbsolutePath, "/data/inbox/" + name);
        message.SetHeader(CaravanHeaders.FileLength, 1234L);
        message.SetHeader(CaravanHeaders.FileLastModified, new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero).ToUnixTimeMilliseconds());
        return exchange;
    }

    [Theory]
    [InlineData("${file:name}", "sub/report.tar.gz")]
    [InlineData("${file:name.ext}", "tar.gz")]
    [InlineData("${file:ext}", "tar.gz")]
    [InlineData("${file:name.ext.single}", "gz")]
    [InlineData("${file:name.noext}", "sub/report")]
    [InlineData("${file:name.noext.single}", "sub/report.tar")]
    [InlineData("${file:onlyname}", "report.tar.gz")]
    [InlineData("${file:onlyname.noext}", "report")]
    [InlineData("${file:onlyname.noext.single}", "report.tar")]
    [InlineData("${file:parent}", "inbox/sub")]
    [InlineData("${file:path}", "inbox/sub/report.tar.gz")]
    [InlineData("${file:absolute}", "False")]
    [InlineData("${file:absolute.path}", "/data/inbox/sub/report.tar.gz")]
    [InlineData("${file:length}", "1234")]
    [InlineData("${file:size}", "1234")]
    [InlineData("${date:file:yyyyMMdd-HHmmss}", "20240304-050607")]
    [InlineData("backup/${file:onlyname.noext}-${date:file:yyyy}.${file:ext}", "backup/report-2024.tar.gz")]
    public void File_functions_read_the_file_headers(string expression, string expected)
    {
        var value = SimpleLanguage.Expression(expression).Evaluate(FileExchange());

        Assert.Equal(expected, Context.TypeConverter.Convert(typeof(string), null, value));
    }

    [Fact]
    public void Typed_values()
    {
        var exchange = FileExchange();

        Assert.Equal(1234L, SimpleLanguage.Expression("${file:size}").Evaluate(exchange));
        Assert.Equal(new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero), SimpleLanguage.Expression("${file:modified}").Evaluate(exchange));
        Assert.Equal(new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero), SimpleLanguage.Expression("${date:file}").Evaluate(exchange));
        Assert.True(SimpleLanguage.Predicate("${file:size} > 1000 && ${file:ext} == 'tar.gz'").Matches(exchange));
    }

    [Fact]
    public void Names_without_extension_or_directory()
    {
        var exchange = FileExchange("plain");

        Assert.Null(SimpleLanguage.Expression("${file:ext}").Evaluate(exchange));
        Assert.Equal("plain", SimpleLanguage.Expression("${file:name.noext}").Evaluate(exchange));
        Assert.Equal("plain", SimpleLanguage.Expression("${file:onlyname}").Evaluate(exchange));
    }

    [Fact]
    public void Onlyname_falls_back_to_the_file_name_header()
    {
        var exchange = new Exchange(Context);
        exchange.Message.SetHeader(CaravanHeaders.FileName, "a/b/c.txt");

        Assert.Equal("c.txt", SimpleLanguage.Expression("${file:onlyname}").Evaluate(exchange));
        Assert.Null(SimpleLanguage.Expression("${file:parent}").Evaluate(exchange));
    }

    [Fact]
    public void Unknown_file_function_is_a_parse_error()
    {
        var e = Assert.Throws<SimpleParseException>(() => SimpleLanguage.Expression("${file:bogus}"));
        Assert.Contains("Unknown file language syntax: bogus", e.Message);
    }

    [Fact]
    public async Task File_functions_on_consumed_files()
    {
        WriteFile("in/sub/data.csv", "1,2,3", new DateTimeOffset(2023, 12, 31, 23, 59, 0, TimeSpan.Zero));
        var mock = Mock("result");
        mock.ExpectedBodiesReceived("sub/data.csv|data|csv|5|20231231");
        await StartWithRoutes(b => b.From(From("in", "recursive=true&noop=true"))
            .SetBody(Expr.Simple("${file:name}|${file:onlyname.noext}|${file:ext}|${file:size}|${date:file:yyyyMMdd}"))
            .To("mock:result"));
        await FirstPollAsync();

        await mock.AssertIsSatisfiedAsync();
    }
}
