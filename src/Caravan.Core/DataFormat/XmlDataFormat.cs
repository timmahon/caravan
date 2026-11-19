using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Caravan.Spi;

namespace Caravan.DataFormat;

/// <summary>
/// The <c>xml</c> data format (Camel's <c>jaxb</c>), built on <see cref="XmlSerializer"/>. Marshal writes <see cref="XDocument"/>,
/// <see cref="XElement"/> and <see cref="XmlNode"/> bodies as they are, text as it is, and serializes other objects with an
/// <see cref="XmlSerializer"/> for their runtime type (without the <c>xsi</c>/<c>xsd</c> namespace declarations). Unmarshal deserializes to
/// <see cref="UnmarshalType"/>, or loads an <see cref="XDocument"/> when none is set. DTDs are prohibited when reading.
/// </summary>
public sealed class XmlDataFormat : IDataFormat, IDataFormatContentType
{
    private static readonly ConcurrentDictionary<Type, XmlSerializer> Serializers = new();
    private static readonly XmlSerializerNamespaces EmptyNamespaces = CreateEmptyNamespaces();

    /// <summary>Creates the data format (untyped unmarshalling to <see cref="XDocument"/>).</summary>
    public XmlDataFormat()
    {
    }

    /// <summary>Creates the data format unmarshalling to <paramref name="unmarshalType"/>.</summary>
    /// <param name="unmarshalType">The type to unmarshal to.</param>
    public XmlDataFormat(Type? unmarshalType)
    {
        UnmarshalType = unmarshalType;
    }

    /// <summary>The type to unmarshal to (serializable with <see cref="XmlSerializer"/>); null loads an <see cref="XDocument"/>.</summary>
    public Type? UnmarshalType { get; set; }

    /// <summary>Indent the marshalled XML (default true, as Camel's <c>jaxb</c>).</summary>
    public bool PrettyPrint { get; set; } = true;

    /// <summary>Leave out the XML declaration (Camel's <c>fragment</c>, default false).</summary>
    public bool Fragment { get; set; }

    /// <summary>The charset of marshalled XML (default UTF-8, without byte order mark).</summary>
    public string? Encoding { get; set; }

    /// <inheritdoc />
    public string? ContentType => "application/xml";

    /// <inheritdoc />
    public bool ContentTypeHeader { get; set; } = true;

    /// <inheritdoc />
    public async ValueTask MarshalAsync(Exchange exchange, object? graph, Stream output)
    {
        var encoding = DataFormatHelper.GetEncoding(Encoding);
        if (graph is string text)
        {
            await output.WriteAsync(encoding.GetBytes(text), exchange.CancellationToken).ConfigureAwait(false);
            return;
        }

        var settings = new XmlWriterSettings
        {
            Indent = PrettyPrint,
            Encoding = encoding,
            OmitXmlDeclaration = Fragment,
            Async = true,
            CloseOutput = false,
        };
        var writer = XmlWriter.Create(output, settings);
        await using (writer.ConfigureAwait(false))
        {
            switch (graph)
            {
                case null:
                    throw new ArgumentException("Cannot marshal a null body to XML");
                case XDocument document:
                    await document.WriteToAsync(writer, exchange.CancellationToken).ConfigureAwait(false);
                    break;
                case XElement element:
                    await element.WriteToAsync(writer, exchange.CancellationToken).ConfigureAwait(false);
                    break;
                case XmlNode node:
                    node.WriteTo(writer);
                    break;
                default:
                    GetSerializer(graph.GetType()).Serialize(writer, graph, EmptyNamespaces);
                    break;
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<object?> UnmarshalAsync(Exchange exchange, Stream input)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            Async = true,
            CloseInput = false,
        };
        using var reader = XmlReader.Create(input, settings);
        if (UnmarshalType is { } type)
        {
            return GetSerializer(type).Deserialize(reader);
        }

        return await XDocument.LoadAsync(reader, LoadOptions.None, exchange.CancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString() => UnmarshalType is null ? "xml" : $"xml[{UnmarshalType.Name}]";

    private static XmlSerializer GetSerializer(Type type) => Serializers.GetOrAdd(type, t => new XmlSerializer(t));

    private static XmlSerializerNamespaces CreateEmptyNamespaces()
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add(string.Empty, string.Empty);
        return namespaces;
    }
}
