namespace Caravan.Model.Rest;

/// <summary>
/// A security scheme of a REST DSL (Camel's <c>securityDefinitions</c>, basic subset: <c>basicAuth</c>, <c>bearerToken</c>, <c>apiKey</c>). It
/// documents the API in the OpenAPI document; enforcing it is the job of the transport (e.g. ASP.NET Core authentication), as in Camel.
/// </summary>
public sealed class RestSecurityDefinition
{
    /// <summary>The key verbs and REST definitions refer to.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The kind of scheme.</summary>
    public RestSecurityType Type { get; set; }

    /// <summary>A description.</summary>
    public string? Description { get; set; }

    /// <summary>For <see cref="RestSecurityType.ApiKey"/>: the name of the header, query parameter or cookie.</summary>
    public string? Name { get; set; }

    /// <summary>For <see cref="RestSecurityType.ApiKey"/>: where the key is: <c>header</c> (default), <c>query</c> or <c>cookie</c>.</summary>
    public string? In { get; set; }

    /// <summary>For <see cref="RestSecurityType.Bearer"/>: a hint of the token format, e.g. <c>JWT</c>.</summary>
    public string? Format { get; set; }
}
