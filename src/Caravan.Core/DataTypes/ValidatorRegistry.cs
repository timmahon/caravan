using Caravan.Support;

namespace Caravan.DataTypes;

/// <summary>The data type validators of a context (Camel's <c>ValidatorRegistry</c>), used by route contracts with validation.</summary>
public sealed class ValidatorRegistry
{
    private readonly CopyOnWriteList<IValidator> _validators = new();

    /// <summary>Creates the registry of a context.</summary>
    /// <param name="context">The context.</param>
    public ValidatorRegistry(ICaravanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    /// <summary>The validators, in the order they were added.</summary>
    public IReadOnlyList<IValidator> All => [.. _validators];

    /// <summary>Adds a validator.</summary>
    /// <param name="validator">The validator.</param>
    /// <returns>This registry.</returns>
    public ValidatorRegistry Add(IValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validators.Add(validator);
        return this;
    }

    /// <summary>Adds a predicate validator: <c>Add("json:Order", Pred.Simple("${body} != null"))</c>.</summary>
    /// <param name="type">The data type (<c>scheme:name</c>).</param>
    /// <param name="predicate">The predicate valid messages satisfy.</param>
    /// <returns>This registry.</returns>
    public ValidatorRegistry Add(string type, IPredicate predicate) => Add(new PredicateValidator(DataType.Parse(type), predicate));

    /// <summary>Removes a validator.</summary>
    /// <param name="validator">The validator.</param>
    /// <returns>Whether it was registered.</returns>
    public bool Remove(IValidator validator) => _validators.Remove(validator);

    /// <summary>Finds the validator of <paramref name="type"/>: an exact one, else one of its scheme; null when there is none.</summary>
    /// <param name="type">The data type.</param>
    /// <returns>The validator.</returns>
    public IValidator? Resolve(DataType type) =>
        _validators.LastOrDefault(v => v.Type == type) ?? _validators.LastOrDefault(v => v.Type.Matches(type));
}
