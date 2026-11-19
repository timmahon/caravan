using System.Text.Json;
using System.Text.Json.Serialization;
using Caravan.Spi;

namespace Caravan.DataFormat;

/// <summary>
/// The <c>json</c> data format (Camel's <c>json-jackson</c>), built on System.Text.Json. Marshal serializes the body with its runtime type;
/// unmarshal deserializes to <see cref="UnmarshalType"/> (a <c>List&lt;T&gt;</c> of it with <see cref="UseList"/>) or, without a type, to plain
/// objects: <see cref="Dictionary{TKey,TValue}"/> of <c>string</c> to <c>object?</c> for objects, <see cref="List{T}"/> of <c>object?</c> for arrays,
/// <c>string</c>, <c>bool</c>, <c>int</c>/<c>long</c>/<c>double</c> and null (like Jackson's maps and lists), so Simple OGNL such as
/// <c>${body[name]}</c> works on the result. Use <c>UnmarshalType = typeof(JsonNode)</c> for a DOM.
/// </summary>
/// <remarks>
/// Differences from Camel: the default serializer options are <see cref="JsonSerializerDefaults.Web"/> (camelCase names, case-insensitive
/// reading), the .NET convention, rather than Jackson's Java field names; <see cref="Include"/> supports <c>ALWAYS</c>, <c>NON_NULL</c> and
/// <c>NON_DEFAULT</c>. Thread-safe once configured; the effective options are built on first use (changing a property rebuilds them).
/// </remarks>
public sealed class JsonDataFormat : IDataFormat, IDataFormatContentType
{
    private Type? _unmarshalType;
    private bool _useList;
    private bool _prettyPrint;
    private string? _include;
    private JsonSerializerOptions? _serializerOptions;
    private JsonSerializerOptions? _effective;

    /// <summary>Creates the data format (untyped unmarshalling).</summary>
    public JsonDataFormat()
    {
    }

    /// <summary>Creates the data format unmarshalling to <paramref name="unmarshalType"/>.</summary>
    /// <param name="unmarshalType">The type to unmarshal to.</param>
    public JsonDataFormat(Type? unmarshalType)
    {
        _unmarshalType = unmarshalType;
    }

    /// <summary>The type to unmarshal to; null unmarshals to dictionaries and lists.</summary>
    public Type? UnmarshalType
    {
        get => _unmarshalType;
        set => _unmarshalType = value;
    }

    /// <summary>Unmarshal to a <c>List&lt;UnmarshalType&gt;</c> (the input is a JSON array).</summary>
    public bool UseList
    {
        get => _useList;
        set => _useList = value;
    }

    /// <summary>Indent the marshalled JSON (default false).</summary>
    public bool PrettyPrint
    {
        get => _prettyPrint;
        set
        {
            _prettyPrint = value;
            _effective = null;
        }
    }

    /// <summary>
    /// Which properties are marshalled (Jackson's <c>include</c>): <c>ALWAYS</c> (default), <c>NON_NULL</c> (null values are left out) or
    /// <c>NON_DEFAULT</c> (null and default values are left out).
    /// </summary>
    /// <exception cref="ArgumentException">Another value.</exception>
    public string? Include
    {
        get => _include;
        set
        {
            _ = ToCondition(value);
            _include = value;
            _effective = null;
        }
    }

    /// <summary>
    /// The serializer options to start from (default <see cref="JsonSerializerDefaults.Web"/>); <see cref="PrettyPrint"/> and
    /// <see cref="Include"/> are applied to a copy.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions
    {
        get => _serializerOptions;
        set
        {
            _serializerOptions = value;
            _effective = null;
        }
    }

    /// <summary>
    /// Whether the <see cref="CaravanHeaders.JsonUnmarshalType"/> header (a <see cref="Type"/> or a type name) may choose the unmarshal type
    /// (default false, as in Camel).
    /// </summary>
    public bool AllowUnmarshallType { get; set; }

    /// <inheritdoc />
    public string? ContentType => "application/json";

    /// <inheritdoc />
    public bool ContentTypeHeader { get; set; } = true;

    /// <summary>The effective serializer options.</summary>
    public JsonSerializerOptions Options => _effective ??= BuildOptions();

    /// <inheritdoc />
    public async ValueTask MarshalAsync(Exchange exchange, object? graph, Stream output)
    {
        var options = Options;
        if (graph is null)
        {
            await JsonSerializer.SerializeAsync<object?>(output, null, options, exchange.CancellationToken).ConfigureAwait(false);
            return;
        }

        await JsonSerializer.SerializeAsync(output, graph, graph.GetType(), options, exchange.CancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<object?> UnmarshalAsync(Exchange exchange, Stream input)
    {
        var type = ResolveUnmarshalType(exchange);
        if (type is not null)
        {
            if (UseList)
            {
                type = typeof(List<>).MakeGenericType(type);
            }

            return await JsonSerializer.DeserializeAsync(input, type, Options, exchange.CancellationToken).ConfigureAwait(false);
        }

        using var document = await JsonDocument.ParseAsync(input, default, exchange.CancellationToken).ConfigureAwait(false);
        return ToObject(document.RootElement);
    }

    /// <summary>Converts a JSON element to plain objects (dictionaries, lists, strings, numbers, booleans and null).</summary>
    /// <param name="element">The element.</param>
    /// <returns>The value.</returns>
    public static object? ToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Aggregate(
            new Dictionary<string, object?>(StringComparer.Ordinal),
            (map, property) =>
            {
                map[property.Name] = ToObject(property.Value);
                return map;
            }),
        JsonValueKind.Array => element.EnumerateArray().Select(ToObject).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt32(out var i) => i,
        JsonValueKind.Number when element.TryGetInt64(out var l) => l,
        JsonValueKind.Number => element.GetDouble(),
        _ => null,
    };

    /// <inheritdoc />
    public override string ToString() => UnmarshalType is null ? "json" : $"json[{UnmarshalType.Name}]";

    private static JsonIgnoreCondition ToCondition(string? include) => include?.Trim().ToUpperInvariant() switch
    {
        null or "" or "ALWAYS" => JsonIgnoreCondition.Never,
        "NON_NULL" or "NON_ABSENT" or "NON_EMPTY" => JsonIgnoreCondition.WhenWritingNull,
        "NON_DEFAULT" => JsonIgnoreCondition.WhenWritingDefault,
        _ => throw new ArgumentException($"Unsupported include: {include} (use ALWAYS, NON_NULL or NON_DEFAULT)", nameof(include)),
    };

    private JsonSerializerOptions BuildOptions()
    {
        var options = _serializerOptions is null ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions(_serializerOptions);
        if (_prettyPrint)
        {
            options.WriteIndented = true;
        }

        if (_include is not null)
        {
            options.DefaultIgnoreCondition = ToCondition(_include);
        }

        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private Type? ResolveUnmarshalType(Exchange exchange)
    {
        if (AllowUnmarshallType && exchange.Message.GetHeader(CaravanHeaders.JsonUnmarshalType) is { } header)
        {
            return header as Type ?? (Type)exchange.Context.TypeConverter.Convert(typeof(Type), exchange, header)!;
        }

        return UnmarshalType;
    }
}
