using Caravan.Spi;

namespace Caravan.Saga;

/// <summary>A step taking part in a saga: what a <c>saga</c> EIP registers with the coordinator (Camel's <c>CamelSagaStep</c>).</summary>
/// <param name="Compensation">Called when the saga is compensated, or null.</param>
/// <param name="Completion">Called when the saga completes, or null.</param>
/// <param name="Options">Values evaluated when the step begins and sent as headers to <paramref name="Compensation"/> and <paramref name="Completion"/>.</param>
/// <param name="Timeout">When set, the saga is compensated if it is still running this long after the step began.</param>
public sealed record SagaStep(IEndpoint? Compensation, IEndpoint? Completion, IReadOnlyDictionary<string, IExpression> Options, TimeSpan? Timeout);
