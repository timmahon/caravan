using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>
/// The <c>rollback</c> EIP: marks the exchange rollback-only and fails it with a <see cref="RollbackExchangeException"/>; with
/// <see cref="MarkRollbackOnly"/> or <see cref="MarkRollbackOnlyLast"/> it only marks the exchange (no exception), which stops routing
/// and completes the unit of work as failed.
/// </summary>
public sealed class RollbackDefinition : ProcessorDefinition
{
    /// <summary>The message of the <see cref="RollbackExchangeException"/>.</summary>
    public string? Message { get; set; }

    /// <summary>Only marks the whole exchange rollback-only, without an exception.</summary>
    public bool MarkRollbackOnly { get; set; }

    /// <summary>Only marks the current route rollback-only (callers continue), without an exception.</summary>
    public bool MarkRollbackOnlyLast { get; set; }

    /// <inheritdoc />
    public override string ShortName => "rollback";

    /// <inheritdoc />
    public override string Label =>
        MarkRollbackOnlyLast ? "markRollbackOnlyLast" : MarkRollbackOnly ? "markRollbackOnly" : Message is null ? "rollback" : $"rollback[{Message}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new RollbackProcessor(Message, MarkRollbackOnly, MarkRollbackOnlyLast);
}
