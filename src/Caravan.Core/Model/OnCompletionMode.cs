namespace Caravan.Model;

/// <summary>When an <c>onCompletion</c> block runs.</summary>
public enum OnCompletionMode
{
    /// <summary>
    /// After the exchange is complete (its unit of work is done), on a correlated copy: it cannot change the reply. The default.
    /// </summary>
    AfterConsumer,

    /// <summary>
    /// When the route that registered it has finished, before the reply goes back to the consumer, on the exchange itself:
    /// it can change the reply.
    /// </summary>
    BeforeConsumer,
}
