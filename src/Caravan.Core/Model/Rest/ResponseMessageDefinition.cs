namespace Caravan.Model.Rest;

/// <summary>A documented response of a REST verb (Camel's <c>ResponseMessageDefinition</c>), for the OpenAPI document.</summary>
public sealed class ResponseMessageDefinition
{
    /// <summary>The status code (<c>200</c>, <c>404</c>...) or <c>default</c>.</summary>
    public string Code { get; set; } = "200";

    /// <summary>The description of the response.</summary>
    public string? Message { get; set; }

    /// <summary>The type of the response body, if any.</summary>
    public Type? ResponseModel { get; set; }

    /// <summary>The response headers.</summary>
    public List<ResponseHeaderDefinition> Headers { get; } = [];
}
