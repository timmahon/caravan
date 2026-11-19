using Caravan.Support;

namespace Caravan.Components.Languages;

/// <summary>Evaluates the endpoint's script and sets the result as the body (when <see cref="LanguageEndpoint.Transform"/>).</summary>
internal sealed class LanguageProducer(LanguageEndpoint endpoint) : ProducerBase(endpoint)
{
    public override ValueTask ProcessAsync(Exchange exchange)
    {
        var result = endpoint.ExpressionFor(exchange).Evaluate(exchange);
        if (endpoint.Transform)
        {
            exchange.Message.Body = result;
        }

        return ValueTask.CompletedTask;
    }
}
