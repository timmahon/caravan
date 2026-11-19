using Caravan.DataFormat;

namespace Caravan;

/// <summary>
/// Factories of the built-in data formats, for <c>marshal</c>/<c>unmarshal</c>:
/// <code>
/// b.From("direct:in").Unmarshal(DataFormats.Json&lt;Order&gt;()).Process(...).Marshal(DataFormats.Xml()).To("mock:out");
/// </code>
/// Each call creates a new instance; for options without a parameter here, create the data format directly, e.g.
/// <c>new CsvDataFormat { UseMaps = true, Trim = true }</c>.
/// </summary>
public static class DataFormats
{
    /// <summary>The <c>json</c> data format, unmarshalling to dictionaries and lists.</summary>
    /// <param name="prettyPrint">Indent marshalled JSON.</param>
    /// <returns>The data format.</returns>
    public static JsonDataFormat Json(bool prettyPrint = false) => new() { PrettyPrint = prettyPrint };

    /// <summary>The <c>json</c> data format unmarshalling to <typeparamref name="T"/> (or a <c>List&lt;T&gt;</c> with <paramref name="useList"/>).</summary>
    /// <typeparam name="T">The type to unmarshal to.</typeparam>
    /// <param name="useList">Unmarshal a JSON array to a list.</param>
    /// <param name="prettyPrint">Indent marshalled JSON.</param>
    /// <returns>The data format.</returns>
    public static JsonDataFormat Json<T>(bool useList = false, bool prettyPrint = false) =>
        new(typeof(T)) { UseList = useList, PrettyPrint = prettyPrint };

    /// <summary>The <c>xml</c> data format, unmarshalling to an <see cref="System.Xml.Linq.XDocument"/>.</summary>
    /// <returns>The data format.</returns>
    public static XmlDataFormat Xml() => new();

    /// <summary>The <c>xml</c> data format unmarshalling to <typeparamref name="T"/> with <see cref="System.Xml.Serialization.XmlSerializer"/>.</summary>
    /// <typeparam name="T">The type to unmarshal to.</typeparam>
    /// <returns>The data format.</returns>
    public static XmlDataFormat Xml<T>() => new(typeof(T));

    /// <summary>The <c>csv</c> data format.</summary>
    /// <param name="delimiter">The field delimiter.</param>
    /// <param name="useMaps">Unmarshal records to dictionaries keyed by the header.</param>
    /// <param name="header">The column names, comma-separated.</param>
    /// <returns>The data format.</returns>
    public static CsvDataFormat Csv(char delimiter = ',', bool useMaps = false, string? header = null) =>
        new() { Delimiter = delimiter, UseMaps = useMaps, Header = header };

    /// <summary>The <c>base64</c> data format.</summary>
    /// <param name="lineLength">The maximum line length of encoded output (0 for one line).</param>
    /// <param name="urlSafe">Use the URL-safe alphabet without padding.</param>
    /// <returns>The data format.</returns>
    public static Base64DataFormat Base64(int lineLength = 76, bool urlSafe = false) => new() { LineLength = lineLength, UrlSafe = urlSafe };

    /// <summary>The <c>gzipDeflater</c> data format.</summary>
    /// <returns>The data format.</returns>
    public static GzipDeflaterDataFormat GzipDeflater() => new();

    /// <summary>The <c>zipDeflater</c> data format (zlib).</summary>
    /// <param name="compressionLevel">The compression level as in Java's <c>Deflater</c> (-1 default).</param>
    /// <returns>The data format.</returns>
    public static ZipDeflaterDataFormat ZipDeflater(int compressionLevel = -1) => new() { CompressionLevel = compressionLevel };

    /// <summary>The <c>string</c> data format.</summary>
    /// <param name="charset">The charset (default UTF-8).</param>
    /// <returns>The data format.</returns>
    public static StringDataFormat String(string? charset = null) => new(charset);
}
