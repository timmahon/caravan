namespace Caravan.Components.File;

/// <summary>
/// The strategy of <c>delete=true</c> (Camel's <c>GenericFileDeleteProcessStrategy</c>): the file is deleted after a successful exchange;
/// <see cref="BeginRenamer"/> (<c>preMove</c>) and <see cref="FailureRenamer"/> (<c>moveFailed</c>) apply as with the rename strategy.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public sealed class GenericFileDeleteProcessStrategy<T> : GenericFileProcessStrategySupport<T>
{
    /// <summary>The name to move the file to before processing (<c>preMove</c>), or null.</summary>
    public IExpression? BeginRenamer { get; set; }

    /// <summary>The name to move the file to when the exchange failed (<c>moveFailed</c>), or null to leave it.</summary>
    public IExpression? FailureRenamer { get; set; }

    /// <inheritdoc />
    public override async ValueTask<bool> BeginAsync(
        IGenericFileOperations<T> operations,
        GenericFileEndpoint<T> endpoint,
        Exchange exchange,
        GenericFile<T> file,
        CancellationToken cancellationToken)
    {
        if (!await base.BeginAsync(operations, endpoint, exchange, file, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (BeginRenamer is not null)
        {
            Rename(operations, exchange, file, BeginRenamer).BindToExchange(exchange);
        }

        return true;
    }

    /// <inheritdoc />
    public override async ValueTask CommitAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint, Exchange exchange, GenericFile<T> file)
    {
        await base.CommitAsync(operations, endpoint, exchange, file).ConfigureAwait(false);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (operations.DeleteFile(file.AbsoluteFilePath) || !operations.ExistsFile(file.AbsoluteFilePath))
            {
                return;
            }
        }

        throw new GenericFileOperationFailedException($"Cannot delete file: {file}");
    }

    /// <inheritdoc />
    public override async ValueTask RollbackAsync(IGenericFileOperations<T> operations, GenericFileEndpoint<T> endpoint, Exchange exchange, GenericFile<T> file)
    {
        await base.RollbackAsync(operations, endpoint, exchange, file).ConfigureAwait(false);
        if (FailureRenamer is not null)
        {
            Rename(operations, exchange, file, FailureRenamer);
        }
    }
}
