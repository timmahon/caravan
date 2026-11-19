namespace Caravan.DataTypes;

/// <summary>A route's input or output data type (Camel's <c>inputType</c>/<c>outputType</c>) and whether messages are validated against it.</summary>
/// <param name="Type">The data type.</param>
/// <param name="Validate">Whether the message is validated with the context's validator for the type (a missing validator is an error).</param>
public sealed record DataTypeContract(DataType Type, bool Validate = false);
