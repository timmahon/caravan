namespace Caravan.Components.DataSet;

/// <summary>A data set whose messages all have the same body (Camel's <c>SimpleDataSet</c>, default 10 messages).</summary>
public sealed class SimpleDataSet : DataSetSupport
{
    /// <summary>Creates a data set of <paramref name="size"/> messages.</summary>
    /// <param name="size">The number of messages.</param>
    public SimpleDataSet(long size = 10)
    {
        Size = size;
    }

    /// <summary>The body of every message (default <c>&lt;hello&gt;world!&lt;/hello&gt;</c>, as Camel).</summary>
    public object? DefaultBody { get; set; } = "<hello>world!</hello>";

    /// <inheritdoc />
    protected override object? CreateMessageBody(long messageIndex) => DefaultBody;
}
