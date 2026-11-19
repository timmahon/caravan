namespace Caravan.Components.DataSet;

/// <summary>
/// A data set whose bodies come from a list (Camel's <c>ListDataSet</c>): message <c>i</c> has body <c>DefaultBodies[i % count]</c>; the size
/// is the number of bodies unless set.
/// </summary>
public sealed class ListDataSet : DataSetSupport
{
    private long? _size;

    /// <summary>Creates a data set of the given bodies.</summary>
    /// <param name="bodies">The bodies.</param>
    public ListDataSet(IEnumerable<object?> bodies)
    {
        DefaultBodies = [.. bodies];
    }

    /// <summary>The bodies (may be added to; the size follows unless set).</summary>
    public List<object?> DefaultBodies { get; }

    /// <inheritdoc />
    public override long Size
    {
        get => _size ?? DefaultBodies.Count;
        set => _size = value;
    }

    /// <inheritdoc />
    protected override object? CreateMessageBody(long messageIndex) =>
        DefaultBodies.Count == 0 ? null : DefaultBodies[(int)(messageIndex % DefaultBodies.Count)];
}
