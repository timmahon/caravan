namespace Caravan.Language.Simple;

/// <summary>
/// A Simple expression or predicate is invalid (Camel's <c>SimpleParserException</c>). <see cref="Index"/> is the zero-based position
/// of the problem in <see cref="Expression"/>; the message ends with the expression and a <c>*</c> under that position.
/// </summary>
public class SimpleParseException : CaravanException
{
    /// <summary>Creates an exception for the problem <paramref name="reason"/> at <paramref name="index"/> in <paramref name="expression"/>.</summary>
    public SimpleParseException(string reason, string expression, int index, Exception? innerException = null)
        : base(Format(reason, expression, index), innerException)
    {
        Reason = reason;
        Expression = expression;
        Index = index;
    }

    /// <summary>What is wrong, without the position.</summary>
    public string Reason { get; }

    /// <summary>The expression text.</summary>
    public string Expression { get; }

    /// <summary>The zero-based position of the problem in <see cref="Expression"/>.</summary>
    public int Index { get; }

    private static string Format(string reason, string expression, int index) =>
        $"{reason} at location {index}{Environment.NewLine}{expression}{Environment.NewLine}{new string(' ', Math.Clamp(index, 0, expression.Length))}*";
}
