using System.Text;

namespace Caravan.Core.Tests;

/// <summary>The tokenizer expressions (Camel's <c>tokenize</c>).</summary>
public class TokenizeTests : ContextTestBase
{
    [Theory]
    [InlineData("a,b,c", new[] { "a", "b", "c" })]
    [InlineData("a,b,c,", new[] { "a", "b", "c" })]
    [InlineData("a,,b", new[] { "a", "", "b" })]
    [InlineData(",a", new[] { "", "a" })]
    [InlineData("abc", new[] { "abc" })]
    [InlineData("", new string[0])]
    public void Tokenizes_strings(string body, string[] expected)
    {
        Assert.Equal(expected, Tokens(Expr.Tokenize(","), body));
    }

    [Fact]
    public void Null_body_gives_null()
    {
        Assert.Null(Expr.Tokenize(",").Evaluate(new Exchange(Context)));
    }

    [Fact]
    public void Multi_character_and_regex_tokens()
    {
        Assert.Equal(["a", "b", "c"], Tokens(Expr.Tokenize("||"), "a||b||c"));
        Assert.Equal(["a", "b", "c"], Tokens(Expr.Tokenize(@"\s*\d+\s*", regex: true), "a 1 b22c"));
    }

    [Fact]
    public void Group_and_skip_first()
    {
        Assert.Equal(["1,2", "3,4", "5"], Tokens(Expr.Tokenize(",", group: 2), "1,2,3,4,5"));
        Assert.Equal(["2", "3"], Tokens(Expr.Tokenize(",", skipFirst: true), "1,2,3"));
        Assert.Equal(["a\r\nb", "c"], Tokens(Expr.Lines(group: 2), "a\r\nb\nc"));
    }

    [Fact]
    public void Lines_handle_all_line_endings()
    {
        Assert.Equal(["a", "b", "c", "", "d"], Tokens(Expr.Lines(), "a\r\nb\nc\r\rd\n"));
    }

    [Fact]
    public void Tokenizes_streams_readers_and_bytes_across_chunk_boundaries()
    {
        var lines = Enumerable.Range(0, 2000).Select(i => "line " + i + new string('x', i % 7)).ToList();
        var text = string.Join("\r\n", lines);

        Assert.Equal(lines, Tokens(Expr.Lines(), new MemoryStream(Encoding.UTF8.GetBytes(text))));
        Assert.Equal(lines, Tokens(Expr.Lines(), new StringReader(text)));
        Assert.Equal(lines, Tokens(Expr.Lines(), Encoding.UTF8.GetBytes(text)));
        Assert.Equal(lines, Tokens(Expr.Tokenize("\r\n"), new MemoryStream(Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public async Task Async_enumeration_gives_the_same_tokens()
    {
        var text = string.Join("|", Enumerable.Range(0, 3000).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var exchange = new Exchange(Context) { Message = { Body = new MemoryStream(Encoding.UTF8.GetBytes(text)) } };
        var tokens = Assert.IsAssignableFrom<IAsyncEnumerable<string>>(Expr.Tokenize("|").Evaluate(exchange));

        var result = new List<string>();
        await foreach (var token in tokens)
        {
            result.Add(token);
        }

        Assert.Equal(3000, result.Count);
        Assert.Equal("2999", result[^1]);
    }

    [Fact]
    public void Reads_lazily()
    {
        var reader = new CountingReader(string.Join("\n", Enumerable.Range(0, 100_000)));
        var exchange = new Exchange(Context) { Message = { Body = reader } };
        var tokens = Assert.IsAssignableFrom<IEnumerable<string>>(Expr.Lines().Evaluate(exchange));

        Assert.Equal(["0", "1", "2"], tokens.Take(3));
        Assert.True(reader.CharsRead < 10_000, $"read {reader.CharsRead} characters");
    }

    [Fact]
    public void Extension_methods_tokenize_other_expressions()
    {
        var exchange = new Exchange(Context);
        exchange.Message.SetHeader("csv", "a;b");
        exchange.Message.SetHeader("text", "x\ny");

        Assert.Equal(["a", "b"], Assert.IsAssignableFrom<IEnumerable<string>>(Expr.Header("csv").Tokenize(";").Evaluate(exchange)));
        Assert.Equal(["x", "y"], Assert.IsAssignableFrom<IEnumerable<string>>(Expr.Header("text").TokenizeLines().Evaluate(exchange)));
    }

    private List<string> Tokens(IExpression expression, object body)
    {
        var exchange = new Exchange(Context) { Message = { Body = body } };
        return Assert.IsAssignableFrom<IEnumerable<string>>(expression.Evaluate(exchange)).ToList();
    }

    private sealed class CountingReader(string text) : StringReader(text)
    {
        public int CharsRead { get; private set; }

        public override int Read(char[] buffer, int index, int count)
        {
            var read = base.Read(buffer, index, count);
            CharsRead += read;
            return read;
        }
    }
}
