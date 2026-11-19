using Caravan.Processors;
using Caravan.Spi;

namespace Caravan.Model;

/// <summary>The <c>validate</c> EIP: fails the exchange with a <see cref="PredicateValidationException"/> when the predicate does not match.</summary>
public sealed class ValidateDefinition(IPredicate predicate) : ProcessorDefinition
{
    /// <summary>The condition the exchange must meet.</summary>
    public IPredicate Predicate { get; set; } = predicate;

    /// <inheritdoc />
    public override string ShortName => "validate";

    /// <inheritdoc />
    public override string Label => $"validate[{Predicate}]";

    /// <inheritdoc />
    public override IProcessor CreateProcessor(RouteContext routeContext) => new ValidateProcessor(Predicate);
}
