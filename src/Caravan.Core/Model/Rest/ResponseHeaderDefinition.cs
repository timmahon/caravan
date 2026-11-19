namespace Caravan.Model.Rest;

/// <summary>A header of a documented response (Camel's <c>ResponseHeaderDefinition</c>).</summary>
public sealed class ResponseHeaderDefinition
{
    /// <summary>The header name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A description.</summary>
    public string? Description { get; set; }

    /// <summary>The OpenAPI data type (default <c>string</c>).</summary>
    public string? DataType { get; set; }

    /// <summary>The OpenAPI format.</summary>
    public string? DataFormat { get; set; }

    /// <summary>The values allowed, if restricted.</summary>
    public List<string> AllowableValues { get; } = [];
}
