using Caravan.Model.Rest;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>The effective options of a <see cref="RestBindingProcessor"/> (the definition merged with the REST configuration).</summary>
internal sealed class RestBindingSettings
{
    public string Method { get; init; } = "GET";

    public string? Consumes { get; init; }

    public string? Produces { get; init; }

    public RestBindingMode BindingMode { get; init; }

    public bool SkipBindingOnErrorCode { get; init; }

    public bool ClientRequestValidation { get; init; }

    /// <summary>The CORS headers to add, or null when CORS is off.</summary>
    public IReadOnlyDictionary<string, string>? CorsHeaders { get; init; }

    public IReadOnlyList<RestParamDefinition> Params { get; init; } = [];

    public IDataFormat? JsonUnmarshal { get; init; }

    public IDataFormat? JsonMarshal { get; init; }

    public IDataFormat? XmlUnmarshal { get; init; }

    public IDataFormat? XmlMarshal { get; init; }
}
