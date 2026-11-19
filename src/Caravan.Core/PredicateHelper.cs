namespace Caravan;

/// <summary>Helpers for evaluating predicates.</summary>
public static class PredicateHelper
{
    /// <summary>Camel's truthiness rules: booleans as-is, "true"/"false" strings parsed, null is false, anything else is true.</summary>
    public static bool ToBoolean(Exchange exchange, object? value) => value switch
    {
        null => false,
        bool b => b,
        string s when bool.TryParse(s.Trim(), out var parsed) => parsed,
        string s => s.Length > 0,
        _ => true,
    };
}
