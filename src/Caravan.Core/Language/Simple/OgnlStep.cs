namespace Caravan.Language.Simple;

/// <summary>One step of an OGNL path such as <c>.Address</c>, <c>?.City</c>, <c>.ToUpper()</c> or <c>[0]</c>.</summary>
/// <param name="Kind">The kind of step.</param>
/// <param name="Name">The member name, or the index/key.</param>
/// <param name="Arguments">The method arguments (constants).</param>
/// <param name="NullSafe">Whether the step yields null instead of failing when the value it applies to is null (<c>?.</c>).</param>
internal sealed record OgnlStep(OgnlStepKind Kind, string Name, object?[] Arguments, bool NullSafe)
{
    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        OgnlStepKind.Index => (NullSafe ? "?" : string.Empty) + "[" + Name + "]",
        OgnlStepKind.Method => (NullSafe ? "?." : ".") + Name + "(" + string.Join(", ", Arguments) + ")",
        _ => (NullSafe ? "?." : ".") + Name,
    };
}
