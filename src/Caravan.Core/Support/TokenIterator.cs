using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Caravan.Support;

/// <summary>
/// The tokens of a text source (a string, <see cref="TextReader"/>, <see cref="Stream"/> or byte array), read lazily in chunks: the
/// value of <see cref="Expr.Tokenize(string, bool, int, bool)"/> and <see cref="Expr.Lines(int, bool)"/>. Enumerable synchronously
/// or asynchronously (the splitter reads streams asynchronously); a reader or stream can be enumerated once and is disposed at the end.
/// </summary>
/// <remarks>
/// Tokens are the text between separators; a trailing separator does not produce an empty last token. With a group size above 1,
/// that many tokens are joined (with the separators that were between them) into one value; the first token can be skipped (e.g. a
/// CSV header).
/// </remarks>
internal sealed class TokenIterator : IEnumerable<string>, IAsyncEnumerable<string>
{
    private const int ChunkSize = 4096;
    private readonly Func<TextReader> _open;
    private readonly Regex _separator;
    private readonly int _group;
    private readonly bool _skipFirst;

    private TokenIterator(Func<TextReader> open, Regex separator, int group, bool skipFirst)
    {
        _open = open;
        _separator = separator;
        _group = Math.Max(group, 1);
        _skipFirst = skipFirst;
    }

    /// <summary>The tokens of <paramref name="value"/> separated by matches of <paramref name="separator"/>; null for a null value.</summary>
    public static TokenIterator? Create(Exchange exchange, object? value, Regex separator, int group, bool skipFirst)
    {
        Func<TextReader>? open = value switch
        {
            null => null,
            string text => () => new StringReader(text),
            TextReader reader => () => reader,
            Stream stream => () => new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true),
            byte[] bytes => () => new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true),
            _ => ObjectHelper.ToText(exchange, value) is { } text ? () => new StringReader(text) : null,
        };
        return open is null ? null : new TokenIterator(open, separator, group, skipFirst);
    }

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator()
    {
        using var reader = _open();
        var scanner = new Scanner(_separator);
        var grouper = new Grouper(_group, _skipFirst);
        var buffer = new char[ChunkSize];
        while (true)
        {
            var next = scanner.Next(out var token, out var separator);
            if (next == 0)
            {
                scanner.Append(buffer, reader.Read(buffer, 0, buffer.Length));
                continue;
            }

            if (next < 0)
            {
                break;
            }

            if (grouper.Add(token!, separator) is { } value)
            {
                yield return value;
            }
        }

        if (grouper.Flush() is { } last)
        {
            yield return last;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        using var reader = _open();
        var scanner = new Scanner(_separator);
        var grouper = new Grouper(_group, _skipFirst);
        var buffer = new char[ChunkSize];
        while (true)
        {
            var next = scanner.Next(out var token, out var separator);
            if (next == 0)
            {
                scanner.Append(buffer, await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false));
                continue;
            }

            if (next < 0)
            {
                break;
            }

            if (grouper.Add(token!, separator) is { } value)
            {
                yield return value;
            }
        }

        if (grouper.Flush() is { } last)
        {
            yield return last;
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => $"tokenize[{_separator}]";

    /// <summary>Finds tokens in text appended chunk by chunk.</summary>
    private sealed class Scanner(Regex separator)
    {
        private string _pending = string.Empty;
        private int _position;
        private bool _eof;

        /// <summary>Appends a chunk; a count of 0 marks the end of the input.</summary>
        public void Append(char[] buffer, int count)
        {
            if (count == 0)
            {
                _eof = true;
                return;
            }

            _pending = string.Concat(_pending.AsSpan(_position), buffer.AsSpan(0, count));
            _position = 0;
        }

        /// <summary>1 when a token was found, 0 when more input is needed, -1 at the end.</summary>
        public int Next(out string? token, out string separatorText)
        {
            token = null;
            separatorText = string.Empty;
            var match = separator.Match(_pending, _position);
            while (match.Success && match.Length == 0)
            {
                match = match.NextMatch();
            }

            // A match that reaches the end of the buffer might extend into the next chunk.
            if (match.Success && (_eof || match.Index + match.Length < _pending.Length))
            {
                token = _pending[_position..match.Index];
                separatorText = match.Value;
                _position = match.Index + match.Length;
                return 1;
            }

            if (!_eof)
            {
                return 0;
            }

            if (_position < _pending.Length)
            {
                token = _pending[_position..];
                _position = _pending.Length;
                return 1;
            }

            return -1;
        }
    }

    /// <summary>Skips the first token and joins tokens into groups.</summary>
    private sealed class Grouper(int size, bool skipFirst)
    {
        private readonly StringBuilder _group = new();
        private bool _skip = skipFirst;
        private int _count;
        private string _previousSeparator = string.Empty;

        public string? Add(string token, string separator)
        {
            if (_skip)
            {
                _skip = false;
                return null;
            }

            if (size == 1)
            {
                return token;
            }

            if (_count > 0)
            {
                _group.Append(_previousSeparator);
            }

            _group.Append(token);
            _previousSeparator = separator;
            if (++_count < size)
            {
                return null;
            }

            return Flush();
        }

        public string? Flush()
        {
            if (_count == 0)
            {
                return null;
            }

            var value = _group.ToString();
            _group.Clear();
            _count = 0;
            return value;
        }
    }
}
