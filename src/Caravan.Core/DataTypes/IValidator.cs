namespace Caravan.DataTypes;

/// <summary>Validates a message of a data type (Camel's <c>Validator</c>); throws <see cref="ValidationException"/> when it is invalid.</summary>
public interface IValidator
{
    /// <summary>The data type validated; a type without name matches its whole scheme.</summary>
    DataType Type { get; }

    /// <summary>Validates the message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="type">The type it must conform to.</param>
    /// <returns>A task completing when the message is valid.</returns>
    /// <exception cref="ValidationException">The message is invalid.</exception>
    ValueTask ValidateAsync(Message message, DataType type);
}
