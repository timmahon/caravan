namespace Caravan.Hosting;

/// <summary>Options of <c>AddCaravanHealthChecks()</c>.</summary>
public sealed class CaravanHealthCheckOptions
{
    /// <summary>The prefix of the registered health check names (default <c>caravan</c>: <c>caravan-context</c>, <c>caravan-routes</c>, ...).</summary>
    public string NamePrefix { get; set; } = "caravan";

    /// <summary>The tag of the liveness checks (default <c>live</c>): the context check.</summary>
    public string LivenessTag { get; set; } = "live";

    /// <summary>The tag of the readiness checks (default <c>ready</c>): all of them.</summary>
    public string ReadinessTag { get; set; } = "ready";

    /// <summary>Tags added to every check (default <c>caravan</c>).</summary>
    public IList<string> Tags { get; } = ["caravan"];

    /// <summary>The timeout of each check (default none).</summary>
    public TimeSpan? Timeout { get; set; }
}
