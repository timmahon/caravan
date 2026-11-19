namespace Caravan.DataTypes;

/// <summary>Validates with a predicate (Camel's <c>predicateValidator</c>): a message for which it is false is invalid.</summary>
/// <param name="type">The data type validated.</param>
/// <param name="predicate">The predicate.</param>
public sealed class PredicateValidator(DataType type, IPredicate predicate) : IValidator
{
    /// <inheritdoc />
    public DataType Type { get; } = type;

    /// <summary>The predicate.</summary>
    public IPredicate Predicate { get; } = predicate;

    /// <inheritdoc />
    public ValueTask ValidateAsync(Message message, DataType type)
    {
        bool valid;
        try
        {
            valid = Predicate.Matches(message.Exchange);
        }
        catch (Exception e)
        {
            throw new ValidationException($"Validation of {type} failed: {e.Message}", message.Exchange, e);
        }

        return valid
            ? ValueTask.CompletedTask
            : throw new ValidationException($"Validation of {type} failed: {Predicate} is false", message.Exchange);
    }

    /// <inheritdoc />
    public override string ToString() => $"PredicateValidator[{Type}, {Predicate}]";
}
