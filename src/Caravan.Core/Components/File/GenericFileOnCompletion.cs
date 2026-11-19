using Caravan.Spi;

namespace Caravan.Components.File;

/// <summary>
/// Completes a consumed file when its exchange's unit of work is done (Camel's <c>GenericFileOnCompletion</c>): on success the idempotent key is
/// added, the done file deleted and the process strategy committed (move, delete); on failure the strategy rolls back (moveFailed). The file
/// then leaves the in-progress repository. Handed over with the exchange (e.g. to <c>seda</c>), so the file is completed when the work is.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
internal sealed class GenericFileOnCompletion<T>(
    GenericFileEndpoint<T> endpoint,
    IGenericFileOperations<T> operations,
    IGenericFileProcessStrategy<T> processStrategy,
    GenericFile<T> file,
    string absoluteFileName,
    string? idempotentKey,
    IExceptionHandler exceptionHandler) : ISynchronization
{
    public ValueTask OnCompleteAsync(Exchange exchange) => CompleteAsync(exchange, success: true);

    public ValueTask OnFailureAsync(Exchange exchange) => CompleteAsync(exchange, success: false);

    private async ValueTask CompleteAsync(Exchange exchange, bool success)
    {
        try
        {
            if (success)
            {
                if (idempotentKey is not null)
                {
                    await endpoint.GetOrCreateIdempotentRepository().AddAsync(idempotentKey).ConfigureAwait(false);
                }

                DeleteDoneFile(exchange);
                await processStrategy.CommitAsync(operations, endpoint, exchange, file).ConfigureAwait(false);
            }
            else
            {
                await processStrategy.RollbackAsync(operations, endpoint, exchange, file).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            exceptionHandler.HandleException(
                $"Error during {(success ? "commit" : "rollback")} of file {file} on {endpoint.EndpointUri}: {e.Message}",
                exchange,
                e);
        }
        finally
        {
            await endpoint.InProgressRepository.RemoveAsync(absoluteFileName).ConfigureAwait(false);
        }
    }

    private void DeleteDoneFile(Exchange exchange)
    {
        if (endpoint.DoneFileName is not { } pattern || endpoint.Noop)
        {
            return;
        }

        // A done file per file (dynamic name) goes with it; a shared one once the whole batch is done.
        var dynamic = pattern.Contains("{file:name", StringComparison.Ordinal);
        if (dynamic || exchange.GetProperty(ExchangePropertyKeys.BatchComplete, false))
        {
            operations.DeleteFile(endpoint.CreateDoneFileName(absoluteFileName));
        }
    }
}
