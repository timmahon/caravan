namespace Caravan.Processors;

/// <summary>
/// The flags of an exchange that end routing (see <see cref="Pipeline.ContinueProcessing"/>), captured before running a block so that
/// only what the block itself changed is taken into account.
/// </summary>
internal readonly record struct RoutingFlags(bool Failed, bool Stopped, bool RollbackOnly, bool RollbackOnlyLast, bool Handled)
{
    public static RoutingFlags Capture(Exchange exchange) =>
        new(exchange.IsFailed, exchange.IsRouteStop, exchange.IsRollbackOnly, exchange.IsRollbackOnlyLast, exchange.ErrorHandlerHandled);

    /// <summary>Whether the exchange was failed, stopped, marked rollback-only or handled since the flags were captured.</summary>
    public bool Interrupted(Exchange exchange) =>
        (exchange.IsFailed && !Failed) || (exchange.IsRouteStop && !Stopped) || (exchange.IsRollbackOnly && !RollbackOnly) ||
        (exchange.IsRollbackOnlyLast && !RollbackOnlyLast) || (exchange.ErrorHandlerHandled && !Handled);
}
