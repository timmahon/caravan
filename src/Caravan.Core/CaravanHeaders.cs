namespace Caravan;

/// <summary>Well-known header names.</summary>
public static class CaravanHeaders
{
    /// <summary>Overrides the logger category used when logging an exchange.</summary>
    public const string LoggerName = "CaravanLogger";

    /// <summary>The time the message was created, in Unix milliseconds (set by <c>timer</c> and <c>scheduler</c> with <c>includeMetadata</c>).</summary>
    public const string MessageTimestamp = "CaravanMessageTimestamp";

    /// <summary>When the <c>timer</c> or <c>scheduler</c> fired (header name kept from Camel).</summary>
    public const string FiredTime = "firedTime";

    /// <summary>True when the message is being redelivered by an error handler (Camel's <c>Exchange.REDELIVERED</c>).</summary>
    public const string Redelivered = "CaravanRedelivered";

    /// <summary>The number of the current redelivery attempt, starting at 1 (Camel's <c>Exchange.REDELIVERY_COUNTER</c>).</summary>
    public const string RedeliveryCounter = "CaravanRedeliveryCounter";

    /// <summary>The maximum number of redeliveries, when limited (Camel's <c>Exchange.REDELIVERY_MAX_COUNTER</c>).</summary>
    public const string RedeliveryMaxCounter = "CaravanRedeliveryMaxCounter";

    /// <summary>
    /// The id of the saga the exchange belongs to (Camel's <c>Exchange.SAGA_LONG_RUNNING_ACTION</c>, the LRA header). Set by the <c>saga</c> EIP
    /// and sent to compensation and completion endpoints.
    /// </summary>
    public const string SagaLongRunningAction = "Long-Running-Action";

    /// <summary>
    /// The URI of the endpoint intercepted by <c>interceptSendToEndpoint</c>, or of the route's endpoint for <c>interceptFrom</c>
    /// (Camel's <c>Exchange.INTERCEPTED_ENDPOINT</c>; <c>interceptFrom</c> also sets it as an exchange property).
    /// </summary>
    public const string InterceptedEndpoint = "CaravanInterceptedEndpoint";

    /// <summary>The media type of the body (Camel's <c>Exchange.CONTENT_TYPE</c>), set by <c>marshal</c> with data formats such as <c>json</c>.</summary>
    public const string ContentType = "Content-Type";

    /// <summary>The type to unmarshal JSON to, when the <c>json</c> data format allows it (Camel's <c>CamelJacksonUnmarshalType</c>).</summary>
    public const string JsonUnmarshalType = "CaravanJacksonUnmarshalType";

    /// <summary>The index of a message produced by a <c>dataset</c> endpoint (Camel's <c>CamelDataSetIndex</c>).</summary>
    public const string DataSetIndex = "CaravanDataSetIndex";

    /// <summary>
    /// The name of the file relative to the starting directory of a <c>file</c> consumer (<c>sub/hello.txt</c>); on a producer, the name to write
    /// (Camel's <c>CamelFileName</c>).
    /// </summary>
    public const string FileName = "CaravanFileName";

    /// <summary>The file name without leading directories (<c>hello.txt</c>, Camel's <c>CamelFileNameOnly</c>).</summary>
    public const string FileNameOnly = "CaravanFileNameOnly";

    /// <summary>The name of the file as consumed, kept when the route changes <see cref="FileName"/> (Camel's <c>CamelFileNameConsumed</c>).</summary>
    public const string FileNameConsumed = "CaravanFileNameConsumed";

    /// <summary>Whether the consumed file's path is absolute (the endpoint's directory was absolute; Camel's <c>CamelFileAbsolute</c>).</summary>
    public const string FileAbsolute = "CaravanFileAbsolute";

    /// <summary>The absolute path of the consumed file (Camel's <c>CamelFileAbsolutePath</c>).</summary>
    public const string FileAbsolutePath = "CaravanFileAbsolutePath";

    /// <summary>The path of the consumed file: the starting directory as configured joined with the relative name (Camel's <c>CamelFilePath</c>).</summary>
    public const string FilePath = "CaravanFilePath";

    /// <summary>The path of the consumed file relative to the starting directory (Camel's <c>CamelFileRelativePath</c>).</summary>
    public const string FileRelativePath = "CaravanFileRelativePath";

    /// <summary>The directory holding the consumed file, starting with the starting directory as configured (Camel's <c>CamelFileParent</c>).</summary>
    public const string FileParent = "CaravanFileParent";

    /// <summary>The length of the consumed file in bytes, a <see cref="long"/> (Camel's <c>CamelFileLength</c>).</summary>
    public const string FileLength = "CaravanFileLength";

    /// <summary>When the consumed file was last modified, in Unix milliseconds (Camel's <c>CamelFileLastModified</c>).</summary>
    public const string FileLastModified = "CaravanFileLastModified";

    /// <summary>The absolute path of the file a <c>file</c> producer wrote (Camel's <c>CamelFileNameProduced</c>).</summary>
    public const string FileNameProduced = "CaravanFileNameProduced";

    /// <summary>
    /// A file name (or <see cref="IExpression"/>) that a <c>file</c> producer uses instead of its <c>fileName</c> option and
    /// <see cref="FileName"/>, once: the producer removes it (Camel's <c>CamelOverruleFileName</c>).
    /// </summary>
    public const string OverruleFileName = "CaravanOverruleFileName";

    /// <summary>
    /// The local copy of a remote file, for file components that download before processing (Camel's <c>CamelFileLocalWorkPath</c>); the
    /// <c>file</c> component itself never sets it.
    /// </summary>
    public const string FileLocalWorkPath = "CaravanFileLocalWorkPath";

    /// <summary>The HTTP method of the request, e.g. <c>GET</c> (Camel's <c>CamelHttpMethod</c>).</summary>
    public const string HttpMethod = "CaravanHttpMethod";

    /// <summary>The request URI: the path as received, without the query (Camel's <c>CamelHttpUri</c>).</summary>
    public const string HttpUri = "CaravanHttpUri";

    /// <summary>The full request URL (Camel's <c>CamelHttpUrl</c>).</summary>
    public const string HttpUrl = "CaravanHttpUrl";

    /// <summary>The request path (Camel's <c>CamelHttpPath</c>); with <c>matchOnUriPrefix</c>, the part after the endpoint's path.</summary>
    public const string HttpPath = "CaravanHttpPath";

    /// <summary>The decoded query string, without <c>?</c> (Camel's <c>CamelHttpQuery</c>).</summary>
    public const string HttpQuery = "CaravanHttpQuery";

    /// <summary>The query string as received, without <c>?</c> (Camel's <c>CamelHttpRawQuery</c>).</summary>
    public const string HttpRawQuery = "CaravanHttpRawQuery";

    /// <summary>The HTTP status code of the response, an <see cref="int"/> (Camel's <c>CamelHttpResponseCode</c>).</summary>
    public const string HttpResponseCode = "CaravanHttpResponseCode";

    /// <summary>The reason phrase of the response, if any (Camel's <c>CamelHttpResponseText</c>).</summary>
    public const string HttpResponseText = "CaravanHttpResponseText";

    /// <summary>The <c>Accept</c> request header.</summary>
    public const string Accept = "Accept";
}
