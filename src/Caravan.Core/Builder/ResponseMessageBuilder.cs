using Caravan.Model.Rest;

namespace Caravan.Builder;

/// <summary>Configures a <see cref="ResponseMessageDefinition"/>.</summary>
/// <param name="definition">The response.</param>
public sealed class ResponseMessageBuilder(ResponseMessageDefinition definition)
{
    /// <summary>The response being configured.</summary>
    public ResponseMessageDefinition Definition { get; } = definition;

    /// <summary>Sets the type of the response body.</summary>
    /// <param name="type">The type.</param>
    /// <returns>This builder.</returns>
    public ResponseMessageBuilder ResponseModel(Type type)
    {
        Definition.ResponseModel = type;
        return this;
    }

    /// <summary>Documents a response header.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="description">A description.</param>
    /// <param name="dataType">The OpenAPI data type (default <c>string</c>).</param>
    /// <param name="dataFormat">The OpenAPI format.</param>
    /// <returns>This builder.</returns>
    public ResponseMessageBuilder Header(string name, string? description = null, string? dataType = null, string? dataFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Definition.Headers.Add(new ResponseHeaderDefinition { Name = name, Description = description, DataType = dataType, DataFormat = dataFormat });
        return this;
    }
}
