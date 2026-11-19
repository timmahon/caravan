using Caravan.Model;
using Caravan.Saga;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The <c>saga</c> EIP (Camel's <c>SagaProcessor</c> family). The current saga is the one named by the
/// <see cref="CaravanHeaders.SagaLongRunningAction"/> header (when the saga service still knows it). Depending on <see cref="Propagation"/> the
/// exchange joins it (the step is added to it), starts a new saga (the header is set to its id), or runs outside any saga (the header is removed
/// for the block). A saga started here in <see cref="SagaCompletionMode.Auto"/> mode is completed when the scope ends successfully and
/// compensated when it ends failed (exception or rollback), after which the previous saga header is restored; in
/// <see cref="SagaCompletionMode.Manual"/> mode it stays open and the header stays set. Joining never ends the joined saga: its owner does.
/// </summary>
/// <remarks>
/// A failure to complete the saga (e.g. it was compensated meanwhile, by <c>saga:compensate</c> or a timeout) fails the exchange, as in Camel.
/// </remarks>
public sealed class SagaProcessor : ServiceSupport, INavigableProcessor
{
    private readonly ICaravanContext _context;

    internal SagaProcessor(ICaravanContext context, ISagaService service, IProcessor? block, SagaPropagation propagation, SagaCompletionMode completionMode, SagaStep step)
    {
        _context = context;
        Service = service;
        Block = block;
        Propagation = propagation;
        CompletionMode = completionMode;
        Step = step;
    }

    /// <summary>The saga service.</summary>
    public ISagaService Service { get; }

    /// <summary>The saga block, or null when the saga covers the rest of the exchange.</summary>
    public IProcessor? Block { get; }

    /// <summary>How the EIP relates to the current saga.</summary>
    public SagaPropagation Propagation { get; }

    /// <summary>When a saga started here ends.</summary>
    public SagaCompletionMode CompletionMode { get; }

    /// <summary>The step registered with the saga.</summary>
    public SagaStep Step { get; }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => Block is null ? [] : [Block];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var previousId = exchange.Message.GetHeader(CaravanHeaders.SagaLongRunningAction)?.ToString();
        var current = string.IsNullOrEmpty(previousId) ? null : await Service.GetSagaAsync(previousId).ConfigureAwait(false);
        switch (Propagation)
        {
            case SagaPropagation.Required when current is not null:
            case SagaPropagation.Supports when current is not null:
                await JoinAsync(exchange, current).ConfigureAwait(false);
                break;
            case SagaPropagation.Required:
            case SagaPropagation.RequiresNew:
                await StartNewAsync(exchange, previousId).ConfigureAwait(false);
                break;
            case SagaPropagation.Mandatory:
                if (current is null)
                {
                    throw new CaravanExchangeException("Current exchange is not bound to a saga: a saga is mandatory", exchange);
                }

                await JoinAsync(exchange, current).ConfigureAwait(false);
                break;
            case SagaPropagation.Never when current is not null:
                throw new CaravanExchangeException($"Current exchange is bound to saga {current.Id}: no saga is allowed (propagation Never)", exchange);
            default:
                await RunOutsideSagaAsync(exchange, previousId).ConfigureAwait(false);
                break;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Saga[{Propagation}, {CompletionMode}{(Block is null ? string.Empty : ", " + Block)}]";

    /// <inheritdoc />
    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        if (Service is IService)
        {
            await _context.AddServiceAsync(Service, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override Task DoStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsFailure(Exchange exchange) => exchange.IsFailed || exchange.IsRollbackOnly || exchange.IsRollbackOnlyLast;

    private static void RestoreHeader(Exchange exchange, string? previousId)
    {
        if (string.IsNullOrEmpty(previousId))
        {
            exchange.Message.RemoveHeader(CaravanHeaders.SagaLongRunningAction);
        }
        else
        {
            exchange.Message.SetHeader(CaravanHeaders.SagaLongRunningAction, previousId);
        }
    }

    private async ValueTask JoinAsync(Exchange exchange, ISagaCoordinator saga)
    {
        await saga.BeginStepAsync(exchange, Step).ConfigureAwait(false);
        await RunBlockAsync(exchange).ConfigureAwait(false);
    }

    private async ValueTask StartNewAsync(Exchange exchange, string? previousId)
    {
        var saga = await Service.NewSagaAsync(exchange).ConfigureAwait(false);
        exchange.Message.SetHeader(CaravanHeaders.SagaLongRunningAction, saga.Id);
        try
        {
            await saga.BeginStepAsync(exchange, Step).ConfigureAwait(false);
        }
        catch
        {
            // Nothing took part yet: end the saga so it is not left running.
            await saga.CompensateAsync(exchange).ConfigureAwait(false);
            RestoreHeader(exchange, previousId);
            throw;
        }

        if (Block is null)
        {
            if (CompletionMode == SagaCompletionMode.Auto)
            {
                exchange.AddOnCompletion(new SagaCompletionSynchronization(saga));
            }

            return;
        }

        await RunBlockAsync(exchange).ConfigureAwait(false);
        if (CompletionMode == SagaCompletionMode.Manual)
        {
            return;
        }

        try
        {
            if (IsFailure(exchange))
            {
                await saga.CompensateAsync(exchange).ConfigureAwait(false);
            }
            else
            {
                await saga.CompleteAsync(exchange).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            if (exchange.Exception is null)
            {
                exchange.Exception = e;
            }
            else
            {
                exchange.Context.LoggerFactory.CreateLogger<SagaProcessor>().LogWarning(e, "Error compensating saga {SagaId}", saga.Id);
            }
        }

        RestoreHeader(exchange, previousId);
    }

    private async ValueTask RunOutsideSagaAsync(Exchange exchange, string? previousId)
    {
        if (!string.IsNullOrEmpty(previousId))
        {
            exchange.Message.RemoveHeader(CaravanHeaders.SagaLongRunningAction);
        }

        if (Block is null)
        {
            return;
        }

        await RunBlockAsync(exchange).ConfigureAwait(false);
        RestoreHeader(exchange, previousId);
    }

    private async ValueTask RunBlockAsync(Exchange exchange)
    {
        if (Block is null)
        {
            return;
        }

        try
        {
            await Block.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }
    }
}
