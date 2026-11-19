namespace Caravan.Model.Rest;

/// <summary>
/// How the REST DSL binds message bodies (Camel's <c>RestBindingMode</c>): the request body is unmarshalled into the verb's
/// <see cref="VerbDefinition.Type"/> and the response is marshalled back, with the <c>json</c> and/or <c>xml</c> data formats.
/// </summary>
public enum RestBindingMode
{
    /// <summary>No binding (Camel's <c>off</c>, the default): bodies are passed as they are.</summary>
    Off,

    /// <summary>JSON or XML, chosen by the request's <c>Content-Type</c>/<c>Accept</c> headers; no failure when neither applies (<c>auto</c>).</summary>
    Auto,

    /// <summary>JSON only (<c>json</c>).</summary>
    Json,

    /// <summary>XML only (<c>xml</c>).</summary>
    Xml,

    /// <summary>JSON or XML, chosen by the headers (<c>json_xml</c>).</summary>
    JsonXml,
}
