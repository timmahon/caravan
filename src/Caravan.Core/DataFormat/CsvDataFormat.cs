using System.Collections;
using System.Text;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.DataFormat;

/// <summary>
/// The <c>csv</c> data format (Camel's <c>camel-csv</c>, RFC 4180 by default). Unmarshal returns a <c>List&lt;List&lt;string&gt;&gt;</c> of
/// records, or with <see cref="UseMaps"/> a <c>List&lt;Dictionary&lt;string, string?&gt;&gt;</c> keyed by the <see cref="Header"/> (or, when none
/// is set, by the first record). Marshal accepts a list of records (each a list of values or a dictionary), a single dictionary or a single
/// list of values, writing one line per record; the header line is written when <see cref="Header"/> is set, unless
/// <see cref="SkipHeaderRecord"/>; dictionaries are written in <see cref="Header"/> order, or in their own order without a header.
/// </summary>
/// <remarks>
/// Fields containing the delimiter, the quote, a line break (or, with <see cref="QuoteAll"/>, every field) are quoted; quotes are doubled
/// inside quotes. When reading, an <see cref="Escape"/> character (none by default) takes the next character literally. Null values are
/// written as empty fields. Thread-safe once configured.
/// </remarks>
public sealed class CsvDataFormat : IDataFormat
{
    /// <summary>The field delimiter (default <c>,</c>).</summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>The quote character (default <c>"</c>).</summary>
    public char Quote { get; set; } = '"';

    /// <summary>An escape character used when reading (none by default).</summary>
    public char? Escape { get; set; }

    /// <summary>The record separator written by marshal (default CRLF, as RFC 4180).</summary>
    public string RecordSeparator { get; set; } = "\r\n";

    /// <summary>The column names, comma-separated (e.g. <c>id,name</c>): the header line written by marshal and the keys of maps.</summary>
    public string? Header { get; set; }

    /// <summary>
    /// Unmarshal: skip the first record (the header line) when not using maps. Marshal: do not write the header line.
    /// </summary>
    public bool SkipHeaderRecord { get; set; }

    /// <summary>Unmarshal to a list of dictionaries keyed by the header (the configured one or the first record).</summary>
    public bool UseMaps { get; set; }

    /// <summary>Skip empty lines when reading (default true).</summary>
    public bool IgnoreEmptyLines { get; set; } = true;

    /// <summary>Trim the whitespace around unquoted values when reading (default false).</summary>
    public bool Trim { get; set; }

    /// <summary>Quote every field when writing (default false: only when needed).</summary>
    public bool QuoteAll { get; set; }

    /// <summary>The charset (default UTF-8).</summary>
    public string? Charset { get; set; }

    /// <summary>The configured column names, or null.</summary>
    public IReadOnlyList<string>? HeaderColumns =>
        Header is null ? null : Header.Split(',').Select(h => h.Trim()).ToList();

    /// <inheritdoc />
    public async ValueTask MarshalAsync(Exchange exchange, object? graph, Stream output)
    {
        var header = HeaderColumns;
        var builder = new StringBuilder();
        if (header is not null && !SkipHeaderRecord)
        {
            WriteRecord(builder, header);
        }

        foreach (var record in ToRecords(graph))
        {
            var values = record switch
            {
                IDictionary map when header is not null => header.Select(h => map.Contains(h) ? map[h] : null),
                IDictionary map => map.Values.Cast<object?>(),
                IEnumerable items => items.Cast<object?>(),
                _ => [record],
            };
            WriteRecord(builder, values.Select(v => ObjectHelper.ToText(exchange, v)));
        }

        var bytes = DataFormatHelper.GetEncoding(Charset).GetBytes(builder.ToString());
        await output.WriteAsync(bytes, exchange.CancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<object?> UnmarshalAsync(Exchange exchange, Stream input)
    {
        using var reader = new StreamReader(input, DataFormatHelper.GetEncoding(Charset), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(exchange.CancellationToken).ConfigureAwait(false);
        var records = Parse(text);
        if (!UseMaps)
        {
            return SkipHeaderRecord && records.Count > 0 ? records.Skip(1).ToList() : records;
        }

        var header = HeaderColumns;
        var start = 0;
        if (header is null || SkipHeaderRecord)
        {
            header ??= records.Count > 0 ? records[0] : [];
            start = records.Count > 0 ? 1 : 0;
        }

        var maps = new List<Dictionary<string, string?>>();
        for (var i = start; i < records.Count; i++)
        {
            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var c = 0; c < header.Count; c++)
            {
                map[header[c]] = c < records[i].Count ? records[i][c] : null;
            }

            maps.Add(map);
        }

        return maps;
    }

    /// <summary>Parses CSV text into records.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The records.</returns>
    /// <exception cref="FormatException">A quoted field is not closed.</exception>
    public List<List<string>> Parse(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (quoted)
            {
                if (Escape is { } esc && c == esc && esc != Quote && i + 1 < text.Length)
                {
                    field.Append(text[i + 1]);
                    i += 2;
                }
                else if (c == Quote)
                {
                    if (i + 1 < text.Length && text[i + 1] == Quote)
                    {
                        field.Append(Quote);
                        i += 2;
                    }
                    else
                    {
                        quoted = false;
                        i++;
                    }
                }
                else
                {
                    field.Append(c);
                    i++;
                }

                continue;
            }

            if (Escape is { } escape && c == escape && i + 1 < text.Length)
            {
                field.Append(text[i + 1]);
                i += 2;
            }
            else if (c == Quote && (field.Length == 0 || field.ToString().Trim().Length == 0))
            {
                field.Clear();
                quoted = true;
                wasQuoted = true;
                i++;
            }
            else if (c == Delimiter)
            {
                record.Add(EndField(field, wasQuoted));
                wasQuoted = false;
                i++;
            }
            else if (c is '\r' or '\n')
            {
                record.Add(EndField(field, wasQuoted));
                wasQuoted = false;
                AddRecord(records, record);
                record = [];
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }
            else
            {
                field.Append(c);
                i++;
            }
        }

        if (quoted)
        {
            throw new FormatException("EOF reached before the quoted CSV field was closed");
        }

        if (field.Length > 0 || wasQuoted || record.Count > 0)
        {
            record.Add(EndField(field, wasQuoted));
            AddRecord(records, record);
        }

        return records;
    }

    /// <inheritdoc />
    public override string ToString() => "csv";

    private static IEnumerable<object?> ToRecords(object? graph) => graph switch
    {
        null => [],
        IDictionary map => [map],
        string text => [new object?[] { text }],
        IEnumerable items => IsListOfRecords(items) ? items.Cast<object?>() : [items],
        _ => [new[] { graph }],
    };

    private static bool IsListOfRecords(IEnumerable items)
    {
        foreach (var item in items)
        {
            return item is IDictionary || (item is IEnumerable && item is not string);
        }

        return true;
    }

    private void AddRecord(List<List<string>> records, List<string> record)
    {
        if (IgnoreEmptyLines && record.Count == 1 && record[0].Length == 0)
        {
            return;
        }

        records.Add(record);
    }

    private string EndField(StringBuilder field, bool wasQuoted)
    {
        var value = field.ToString();
        field.Clear();
        return Trim && !wasQuoted ? value.Trim() : value;
    }

    private void WriteRecord(StringBuilder builder, IEnumerable<string?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(Delimiter);
            }

            first = false;
            var text = value ?? string.Empty;
            if (QuoteAll || text.Contains(Delimiter) || text.Contains(Quote) || text.Contains('\r') || text.Contains('\n'))
            {
                builder.Append(Quote).Append(text.Replace(Quote.ToString(), new string(Quote, 2), StringComparison.Ordinal)).Append(Quote);
            }
            else
            {
                builder.Append(text);
            }
        }

        builder.Append(RecordSeparator);
    }
}
