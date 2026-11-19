namespace Caravan.Components.File;

/// <summary>
/// The default strategy (Camel's <c>GenericFileRenameProcessStrategy</c>): moves the file before processing (<see cref="BeginRenamer"/>,
/// <c>preMove</c>; the exchange then refers to the moved file), after success (<see cref="CommitRenamer"/>, <c>move</c>, by default
/// <c>.caravan/</c> next to the file) and after failure (<see cref="FailureRenamer"/>, <c>moveFailed</c>; without it the file stays to be
/// retried at the next poll).
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public sealed class GenericFileRenameProcessStrategy<T> : GenericFileProcessStrategySupport<T>
{
    /// <summary>The name to move the file to before processing (<c>preMove</c>), or null.</summary>
    public IExpression? BeginRenamer { get; set; }

    /// <summary>The name to move the file to after a successful exchange (<c>move</c>), or null to leave it.</summary>
    public IExpression? CommitRenamer { get; set; }

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
        if (CommitRenamer is not null)
        {
            Rename(operations, exchange, file, CommitRenamer);
        }
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
