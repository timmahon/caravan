namespace Caravan.Model;

/// <summary>Something configured with redelivery options: an error handler or an <c>onException</c> clause.</summary>
public interface IRedeliveryPolicyAware
{
    /// <summary>The redelivery options.</summary>
    RedeliveryPolicyDefinition RedeliveryPolicy { get; }
}
