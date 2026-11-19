using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Builder;

/// <summary>DSL for the <c>idempotentConsumer</c> EIP.</summary>
public static class IdempotentConsumerDsl
{
    /// <summary>
    /// Runs <paramref name="block"/> only for messages whose id (<paramref name="messageId"/>) is not in <paramref name="repository"/>:
    /// <c>.IdempotentConsumer(Expr.Header("id"), new MemoryIdempotentRepository(), i =&gt; i.To("mock:once"))</c>. Duplicates get
    /// <see cref="ExchangePropertyKeys.DuplicateMessage"/> and continue after the block.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="messageId">Computes the message id (a null id fails the exchange with <see cref="NoMessageIdException"/>).</param>
    /// <param name="repository">Remembers processed ids.</param>
    /// <param name="block">The steps run once per message id.</param>
    /// <param name="eager">Add the id when the exchange arrives (default; exactly one of concurrent duplicates goes through) rather than when it completes.</param>
    /// <param name="skipDuplicate">Skip the block for duplicates (default); false runs it anyway, with the duplicate property set.</param>
    /// <param name="removeOnFailure">Remove the id when the exchange fails (default), so the message can be retried.</param>
    /// <param name="completionEager">Confirm or remove the id as soon as the block is done rather than when the exchange completes.</param>
    public static T IdempotentConsumer<T>(
        this T steps,
        IExpression messageId,
        IIdempotentRepository repository,
        Action<RouteSteps> block,
        bool eager = true,
        bool skipDuplicate = true,
        bool removeOnFailure = true,
        bool completionEager = false)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(repository);
        var definition = steps.AddStep(new IdempotentConsumerDefinition(messageId)
        {
            Repository = repository,
            Eager = eager,
            SkipDuplicate = skipDuplicate,
            RemoveOnFailure = removeOnFailure,
            CompletionEager = completionEager,
        });
        steps.BuildNested(definition.Outputs, definition, block);
        return steps;
    }

    /// <summary>Runs <paramref name="block"/> only for messages whose id (<paramref name="messageId"/>) is not in <paramref name="repository"/>.</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="messageId">Computes the message id (a null id fails the exchange with <see cref="NoMessageIdException"/>).</param>
    /// <param name="repository">Remembers processed ids.</param>
    /// <param name="block">The steps run once per message id.</param>
    /// <param name="eager">Add the id when the exchange arrives (default) rather than when it completes.</param>
    /// <param name="skipDuplicate">Skip the block for duplicates (default); false runs it anyway, with the duplicate property set.</param>
    /// <param name="removeOnFailure">Remove the id when the exchange fails (default), so the message can be retried.</param>
    /// <param name="completionEager">Confirm or remove the id as soon as the block is done rather than when the exchange completes.</param>
    public static T IdempotentConsumer<T>(
        this T steps,
        Func<Exchange, object?> messageId,
        IIdempotentRepository repository,
        Action<RouteSteps> block,
        bool eager = true,
        bool skipDuplicate = true,
        bool removeOnFailure = true,
        bool completionEager = false)
        where T : RouteSteps =>
        steps.IdempotentConsumer(new FuncExpression(messageId), repository, block, eager, skipDuplicate, removeOnFailure, completionEager);
}
