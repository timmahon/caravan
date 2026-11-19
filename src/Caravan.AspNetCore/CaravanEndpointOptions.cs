namespace Caravan.AspNetCore;

/// <summary>What <c>app.MapCaravan()</c> maps besides the <c>platform-http</c> and REST endpoints.</summary>
public sealed class CaravanEndpointOptions
{
    /// <summary>
    /// The path of the OpenAPI document; null (default) uses the REST configuration's context path and <c>apiContextPath</c>, and maps
    /// nothing when that is not set.
    /// </summary>
    public string? OpenApiPath { get; set; }

    /// <summary>Whether the management API is mapped (default false: it can stop routes, protect it).</summary>
    public bool Management { get; set; }

    /// <summary>The base path of the management API (default <c>/caravan/management</c>).</summary>
    public string ManagementPath { get; set; } = "/caravan/management";

    /// <summary>Whether the health endpoints are mapped (default true).</summary>
    public bool Health { get; set; } = true;

    /// <summary>The path of the health endpoint (default <c>/caravan/health</c>; <c>/live</c> and <c>/ready</c> below it).</summary>
    public string HealthPath { get; set; } = "/caravan/health";
}
