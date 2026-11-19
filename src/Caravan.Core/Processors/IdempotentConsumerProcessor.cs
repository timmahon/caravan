using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The <c>idempotentConsumer</c> EIP (Camel's <c>IdempotentConsumer</c>): runs the nested block only for exchanges whose message id
/// (<see cref="MessageIdExpression"/>, converted to a string) is not in the <see cref="Repository"/>. A duplicate gets
/// <see cref="ExchangePropertyKeys.DuplicateMessage"/> set to true and skips the block (routing continues after it), or runs it anyway when
/// <see cref="SkipDuplicate"/> is false. A null message id fails the exchange with a <see cref="NoMessageIdException"/>.
/// </summary>
/// <remarks>
/// <para>
/// With <see cref="Eager"/> (default) the id is added when the exchange arrives (atomically, so of concurrent duplicates exactly one goes
/// through); otherwise it is only checked then and added when the exchange completed successfully, so concurrent duplicates may all go through.
/// </para>
/// <para>
/// Completion: when the exchange completes (its unit of work is done) or, with <see cref="CompletionEager"/>, as soon as the block is done: on
/// success the id is added (non-eager) and confirmed; on failure it is removed when <see cref="RemoveOnFailure"/> (default), so the message can
/// be retried. A failure an error handler handled counts as success, as in Camel.
/// </para>
/// </remarks>
public sealed class IdempotentConsumerProcessor : ServiceSupport, INavigableProcessor
{
    private long _duplicateCount;

    /// <summary>Creates the processor.</summary>
    /// <param name="block">The nested block.</param>
    /// <param name="messageIdExpression">Computes the message id.</param>
    /// <param name="repository">Remembers processed ids.</param>
    /// <param name="eager">Add the id when the exchange arrives (else when it completes).</param>
    /// <param name="skipDuplicate">Skip the block for duplicates (else only mark them).</param>
    /// <param name="removeOnFailure">Remove the id when the exchange fails.</param>
    /// <param name="completionEager">Complete (confirm or remove the id) when the block is done instead of when the exchange completes.</param>
    public IdempotentConsumerProcessor(
        IProcessor block,
        IExpression messageIdExpression,
        IIdempotentRepository repository,
        bool eager = true,
        bool skipDuplicate = true,
        bool removeOnFailure = true,
        bool completionEager = false)
    {
        Block = block;
        MessageIdExpression = messageIdExpression;
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Eager = eager;
        SkipDuplicate = skipDuplicate;
        RemoveOnFailure = removeOnFailure;
        CompletionEager = completionEager;
    }

    /// <summary>The nested block.</summary>
    public IProcessor Block { get; }

    /// <summary>Computes the message id.</summary>
    public IExpression MessageIdExpression { get; }

    /// <summary>Remembers processed ids.</summary>
    public IIdempotentRepository Repository { get; }

    /// <summary>Whether the id is added when the exchange arrives (else when it completes).</summary>
    public bool Eager { get; }

    /// <summary>Whether duplicates skip the block (else they are only marked).</summary>
    public bool SkipDuplicate { get; }

    /// <summary>Whether the id is removed when the exchange fails.</summary>
    public bool RemoveOnFailure { get; }

    /// <summary>Whether the id is confirmed or removed when the block is done instead of when the exchange completes.</summary>
    public bool CompletionEager { get; }

    /// <summary>The number of duplicates seen since the processor started.</summary>
    public long DuplicateMessageCount => Interlocked.Read(ref _duplicateCount);

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Block];

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var messageId = ObjectHelper.ToText(exchange, MessageIdExpression.Evaluate(exchange))
            ?? throw new NoMessageIdException(exchange, MessageIdExpression);
        var token = exchange.CancellationToken;
        var isNew = Eager
            ? await Repository.AddAsync(messageId, token).ConfigureAwait(false)
            : !await Repository.ContainsAsync(messageId, token).ConfigureAwait(false);
        if (!isNew)
        {
            exchange.SetProperty(ExchangePropertyKeys.DuplicateMessage, true);
            Interlocked.Increment(ref _duplicateCount);
            if (SkipDuplicate)
            {
                exchange.Context.LoggerFactory.CreateLogger<IdempotentConsumerProcessor>()
                    .LogDebug("{MessageId} is a duplicate; skipping exchange {ExchangeId}", messageId, exchange.ExchangeId);
                return;
            }
        }

        var completion = new IdempotentCompletion(this, messageId);
        if (!CompletionEager)
        {
            exchange.AddOnCompletion(completion);
        }

        try
        {
            await Block.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }

        if (CompletionEager)
        {
            if (exchange.IsFailed || exchange.IsRollbackOnly || exchange.IsRollbackOnlyLast)
            {
                await completion.OnFailureAsync(exchange).ConfigureAwait(false);
            }
            else
            {
                await completion.OnCompleteAsync(exchange).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"IdempotentConsumer[{MessageIdExpression}]";

    /// <summary>Starts the repository when it is a service.</summary>
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _duplicateCount, 0);
        return StartServiceAsync(Repository, cancellationToken);
    }

    /// <summary>Stops the repository when it is a service.</summary>
    protected override Task DoStopAsync(CancellationToken cancellationToken) => StopServiceAsync(Repository, cancellationToken);

    /// <summary>Confirms or removes the message id when the exchange completes (Camel's <c>IdempotentOnCompletion</c>).</summary>
    private sealed class IdempotentCompletion(IdempotentConsumerProcessor owner, string messageId) : ISynchronization
    {
        public async ValueTask OnCompleteAsync(Exchange exchange)
        {
            var repository = owner.Repository;
            if (!owner.Eager)
            {
                // Not added when the exchange arrived: add it now that it was processed.
                await repository.AddAsync(messageId).ConfigureAwait(false);
            }

            await repository.ConfirmAsync(messageId).ConfigureAwait(false);
        }

        public async ValueTask OnFailureAsync(Exchange exchange)
        {
            if (owner.RemoveOnFailure)
            {
                await owner.Repository.RemoveAsync(messageId).ConfigureAwait(false);
            }
        }

        public override string ToString() => $"IdempotentCompletion[{messageId}]";
    }
}
