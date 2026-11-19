namespace Caravan.Model.Rest;

/// <summary>Where a REST parameter is carried (Camel's <c>RestParamType</c>).</summary>
public enum RestParamType
{
    /// <summary>A <c>{name}</c> segment of the path (Camel's default).</summary>
    Path,

    /// <summary>A query parameter.</summary>
    Query,

    /// <summary>A request header.</summary>
    Header,

    /// <summary>The request body.</summary>
    Body,

    /// <summary>A form field.</summary>
    FormData,
}
