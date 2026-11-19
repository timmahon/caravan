namespace Caravan.Model.Rest;

/// <summary>
/// A parameter of a REST verb (Camel's <c>RestParamDefinition</c>). It documents the API (OpenAPI) and drives the binding: query
/// parameters with a <see cref="DefaultValue"/> are set as headers when missing, and with client request validation required query
/// parameters and headers, a required body and <see cref="AllowableValues"/> are checked (400 Bad Request).
/// </summary>
public sealed class RestParamDefinition
{
    /// <summary>The parameter name (the header name it is read from).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Where the parameter is (default <see cref="RestParamType.Path"/>, as in Camel).</summary>
    public RestParamType Type { get; set; } = RestParamType.Path;

    /// <summary>Whether the parameter is required (default true, as in Camel).</summary>
    public bool Required { get; set; } = true;

    /// <summary>A default value, used when the parameter is missing.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>The OpenAPI data type: <c>string</c> (default), <c>integer</c>, <c>number</c>, <c>boolean</c>, <c>array</c>... (Camel's <c>dataType</c>).</summary>
    public string? DataType { get; set; }

    /// <summary>The OpenAPI format, e.g. <c>int64</c> or <c>date-time</c> (Camel's <c>dataFormat</c>).</summary>
    public string? DataFormat { get; set; }

    /// <summary>The element type of an <c>array</c> (Camel's <c>arrayType</c>).</summary>
    public string? ArrayType { get; set; }

    /// <summary>The values allowed, if restricted.</summary>
    public List<string> AllowableValues { get; } = [];

    /// <summary>A description.</summary>
    public string? Description { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Type}:{Name}";
}
