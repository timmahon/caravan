using Caravan.Impl;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Caravan.Hosting;

/// <summary>
/// Starts the context when the host starts and stops it gracefully when the host stops (Camel main's lifecycle). The graceful shutdown follows
/// the context's <see cref="Spi.IShutdownStrategy"/> (and its timeout); when the host's shutdown token fires first (the host's own shutdown
/// timeout), the shutdown is forced: inflight exchanges are cancelled through <see cref="ICaravanContext.ShutdownToken"/>.
/// </summary>
/// <param name="context">The context.</param>
/// <param name="logger">The logger.</param>
public sealed class CaravanHostedService(ICaravanContext context, ILogger<CaravanHostedService> logger) : IHostedService
{
    /// <summary>The context this service runs.</summary>
    public ICaravanContext Context { get; } = context;

    /// <summary>Starts the context (and its auto-startup routes).</summary>
    /// <param name="cancellationToken">Cancelled when the host's startup is aborted.</param>
    /// <returns>The start.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting Caravan context {Name}", Context.Name);
        await Context.StartAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Caravan context {Name} started with {Started} of {Routes} route(s) running",
            Context.Name,
            Context.Routes.Count(r => r.Status == Spi.ServiceStatus.Started),
            Context.Routes.Count);
    }

    /// <summary>Stops the context gracefully, forcing the shutdown when <paramref name="cancellationToken"/> fires first.</summary>
    /// <param name="cancellationToken">The host's shutdown token.</param>
    /// <returns>The stop.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping Caravan context {Name}", Context.Name);
        var strategy = Context.ShutdownStrategy as DefaultShutdownStrategy;
        await using var registration = cancellationToken.Register(() =>
        {
            logger.LogWarning("The host's shutdown timeout elapsed before Caravan context {Name} stopped gracefully; forcing the shutdown", Context.Name);
            strategy?.ForceShutdown();
        }).ConfigureAwait(false);
        try
        {
            await Context.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            strategy?.ResetForcedShutdown();
        }

        logger.LogInformation("Caravan context {Name} stopped", Context.Name);
    }
}
