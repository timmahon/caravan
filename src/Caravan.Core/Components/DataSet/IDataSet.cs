namespace Caravan.Components.DataSet;

/// <summary>
/// A source of test messages for the <c>dataset</c> component (Camel's <c>DataSet</c>), bound in the registry: a <c>dataset</c> consumer sends
/// <see cref="Size"/> messages made by <see cref="PopulateMessage"/>, and a <c>dataset</c> producer checks that the messages it receives are
/// those, in order, with <see cref="AssertMessageExpected"/>.
/// </summary>
public interface IDataSet
{
    /// <summary>The number of messages.</summary>
    long Size { get; }

    /// <summary>Progress is logged every this many messages.</summary>
    long ReportCount { get; }

    /// <summary>Fills the message of <paramref name="exchange"/> for the message number <paramref name="messageIndex"/> (zero-based).</summary>
    /// <param name="exchange">The exchange to fill.</param>
    /// <param name="messageIndex">The message number.</param>
    void PopulateMessage(Exchange exchange, long messageIndex);

    /// <summary>Checks that <paramref name="actual"/> is the message <paramref name="messageIndex"/> (<paramref name="expected"/>).</summary>
    /// <param name="endpoint">The endpoint that received the message.</param>
    /// <param name="expected">An exchange filled with <see cref="PopulateMessage"/>.</param>
    /// <param name="actual">The received exchange.</param>
    /// <param name="messageIndex">The message number.</param>
    /// <exception cref="Mock.MockAssertionException">The message is not the expected one.</exception>
    void AssertMessageExpected(DataSetEndpoint endpoint, Exchange expected, Exchange actual, long messageIndex);
}
