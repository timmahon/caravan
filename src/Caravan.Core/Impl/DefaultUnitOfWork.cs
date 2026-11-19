using Caravan.Spi;

namespace Caravan.Impl;

/// <summary>Default <see cref="IUnitOfWork"/>.</summary>
public sealed class DefaultUnitOfWork : IUnitOfWork
{
    private readonly List<ISynchronization> _synchronizations = [];

    /// <summary>
    /// Creates the unit of work for <paramref name="exchange"/>, keeping a copy of the original message when the context allows it
    /// and taking over the callbacks registered with <see cref="Exchange.AddOnCompletion"/>.
    /// </summary>
    public DefaultUnitOfWork(Exchange exchange)
    {
        Exchange = exchange;
        if (exchange.Context.AllowUseOriginalMessage)
        {
            OriginalMessage = exchange.Message.Copy(exchange);
        }

        if (exchange.TakePendingCompletions() is { } pending)
        {
            _synchronizations.AddRange(pending);
        }
    }

    /// <inheritdoc />
    public Exchange Exchange { get; }

    /// <inheritdoc />
    public Message? OriginalMessage { get; }

    /// <inheritdoc />
    public void AddSynchronization(ISynchronization synchronization)
    {
        lock (_synchronizations)
        {
            _synchronizations.Add(synchronization);
        }
    }

    /// <inheritdoc />
    public bool ContainsSynchronization(ISynchronization synchronization)
    {
        lock (_synchronizations)
        {
            return _synchronizations.Contains(synchronization);
        }
    }

    /// <inheritdoc />
    public void RemoveSynchronization(ISynchronization synchronization)
    {
        lock (_synchronizations)
        {
            _synchronizations.Remove(synchronization);
        }
    }

    /// <inheritdoc />
    public void HandoverSynchronization(Exchange target, Func<ISynchronization, bool>? filter = null)
    {
        List<ISynchronization> moved;
        lock (_synchronizations)
        {
            moved = _synchronizations.Where(s => s.AllowHandover && (filter?.Invoke(s) ?? true)).ToList();
            foreach (var s in moved)
            {
                _synchronizations.Remove(s);
            }
        }

        foreach (var s in moved)
        {
            target.AddOnCompletion(s);
        }
    }

    /// <inheritdoc />
    public async ValueTask DoneAsync(Exchange exchange)
    {
        List<ISynchronization> snapshot;
        lock (_synchronizations)
        {
            snapshot = [.. _synchronizations];
            _synchronizations.Clear();
        }

        foreach (var synchronization in snapshot)
        {
            try
            {
                if (exchange.IsFailed || exchange.IsRollbackOnly)
                {
                    await synchronization.OnFailureAsync(exchange).ConfigureAwait(false);
                }
                else
                {
                    await synchronization.OnCompleteAsync(exchange).ConfigureAwait(false);
                }
            }
            catch
            {
                // A failing synchronization must not affect the others.
            }
        }
    }
}
