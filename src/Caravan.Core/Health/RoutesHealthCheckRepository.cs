namespace Caravan.Health;

/// <summary>One <see cref="RouteHealthCheck"/> per runtime route (Camel's <c>RoutesHealthCheckRepository</c>).</summary>
internal sealed class RoutesHealthCheckRepository(ICaravanContext context) : IHealthCheckRepository
{
    public string Id => "routes";

    public IEnumerable<IHealthCheck> GetHealthChecks() => context.Routes.Select(r => new RouteHealthCheck(context, r.Id));
}
