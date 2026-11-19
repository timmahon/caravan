namespace Caravan.Spi;

/// <summary>
/// Implemented by data formats with a media type (Camel's <c>DataFormatContentTypeHeader</c>): when <see cref="ContentTypeHeader"/> is true,
/// <c>marshal</c> sets the <see cref="CaravanHeaders.ContentType"/> header to <see cref="ContentType"/>.
/// </summary>
public interface IDataFormatContentType
{
    /// <summary>The media type of marshalled data, e.g. <c>application/json</c>; null when unknown.</summary>
    string? ContentType { get; }

    /// <summary>Whether <c>marshal</c> sets the <c>Content-Type</c> header (default true, as in Camel).</summary>
    bool ContentTypeHeader { get; set; }
}
