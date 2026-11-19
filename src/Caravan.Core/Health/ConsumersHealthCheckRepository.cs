namespace Caravan.Health;

/// <summary>One <see cref="ConsumerHealthCheck"/> per runtime route (Camel's <c>ConsumersHealthCheckRepository</c>).</summary>
internal sealed class ConsumersHealthCheckRepository(ICaravanContext context) : IHealthCheckRepository
{
    public string Id => "consumers";

    public IEnumerable<IHealthCheck> GetHealthChecks() => context.Routes.Select(r => new ConsumerHealthCheck(context, r.Id));
}
