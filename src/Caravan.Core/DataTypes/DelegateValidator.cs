namespace Caravan.DataTypes;

/// <summary>A validator given as a function returning whether the message is valid.</summary>
/// <param name="type">The data type validated.</param>
/// <param name="isValid">Returns whether the message is valid.</param>
public sealed class DelegateValidator(DataType type, Func<Message, bool> isValid) : IValidator
{
    /// <inheritdoc />
    public DataType Type { get; } = type;

    /// <inheritdoc />
    public ValueTask ValidateAsync(Message message, DataType type) =>
        isValid(message) ? ValueTask.CompletedTask : throw new ValidationException($"Validation of {type} failed", message.Exchange);

    /// <inheritdoc />
    public override string ToString() => $"Validator[{Type}]";
}
