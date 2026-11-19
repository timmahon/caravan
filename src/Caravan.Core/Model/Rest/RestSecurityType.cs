namespace Caravan.Model.Rest;

/// <summary>The kinds of security scheme a REST DSL can declare (documented in the OpenAPI document; not enforced).</summary>
public enum RestSecurityType
{
    /// <summary>HTTP basic authentication (Camel's <c>basicAuth</c>).</summary>
    Basic,

    /// <summary>An HTTP bearer token (Camel's <c>bearerToken</c>).</summary>
    Bearer,

    /// <summary>An API key in a header, query parameter or cookie (Camel's <c>apiKey</c>).</summary>
    ApiKey,
}
