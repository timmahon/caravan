using Caravan.Processors;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Model;

/// <summary>
/// The <c>idempotentConsumer</c> EIP: runs the nested steps only for messages whose id was not seen before; see
/// <see cref="IdempotentConsumerProcessor"/>.
/// </summary>
public sealed class IdempotentConsumerDefinition(IExpression messageIdExpression) : OutputDefinition
{
    /// <summary>Computes the message id.</summary>
    public IExpression MessageIdExpression { get; set; } = messageIdExpression;

    /// <summary>Remembers processed ids.</summary>
    public IIdempotentRepository? Repository { get; set; }

    /// <summary>The name of an <see cref="IIdempotentRepository"/> in the registry, used when <see cref="Repository"/> is null.</summary>
    public string? RepositoryRef { get; set; }

    /// <summary>Whether the id is added when the exchange arrives (default) rather than when it completes.</summary>
    public bool Eager { get; set; } = true;

    /// <summary>Whether duplicates skip the nested steps (default) rather than only being marked with <see cref="ExchangePropertyKeys.DuplicateMessage"/>.</summary>
    public bool SkipDuplicate { get; set; } = true;

    /// <summary>Whether the id is removed when the exchange fails (default), so the message can be retried.</summary>
    public bool RemoveOnFailure { get; set; } = true;

    /// <summary>Whether the id is confirmed or removed as soon as the nested steps are done rather than when the exchange completes.</summary>
    public bool CompletionEager { get; set; }

    /// <inheritdoc />
    public override string ShortName => "idempotentConsumer";

    /// <inheritdoc />
    public override string Label => $"idempotentConsumer[{MessageIdExpression}]";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No repository is configured.</exception>
    public override IProcessor CreateProcessor(RouteContext routeContext)
    {
        var repository = Repository ?? RegistryLookup.Lookup<IIdempotentRepository>(routeContext.Context, RepositoryRef)
            ?? throw new InvalidOperationException("idempotentConsumer: an idempotent repository must be configured");
        return new IdempotentConsumerProcessor(
            routeContext.CreateBlockProcessor(Outputs, this),
            MessageIdExpression,
            repository,
            Eager,
            SkipDuplicate,
            RemoveOnFailure,
            CompletionEager);
    }
}
