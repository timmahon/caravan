namespace Caravan.Model.Rest;

/// <summary>A security requirement of a REST definition or verb (Camel's <c>security</c>): the key of a <see cref="RestSecurityDefinition"/> and scopes.</summary>
public sealed class RestSecurityRequirement
{
    /// <summary>The key of the security definition.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The scopes required (comma-separated in Camel), if any.</summary>
    public List<string> Scopes { get; } = [];
}
