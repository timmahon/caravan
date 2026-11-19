using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Processors;

/// <summary>
/// Camel's <c>from</c> with <c>variableReceive</c>: moves the consumed message into a variable (body as the variable, headers as
/// <c>name.header.key</c>) and starts the route with an empty message.
/// </summary>
internal sealed class FromVariableProcessor(string variable, IProcessor next) : INavigableProcessor
{
    public IReadOnlyList<IProcessor> Next => [next];

    public static IProcessor Wrap(Model.RouteDefinition route, IProcessor pipeline) =>
        route.FromVariableReceive is { } variable ? new FromVariableProcessor(variable, pipeline) : pipeline;

    public ValueTask ProcessAsync(Exchange exchange)
    {
        VariableSendReceive.Store(exchange, variable, exchange.Message);
        exchange.Message.Body = null;
        exchange.Message.SetHeaders(new Dictionary<string, object?>());
        return next.ProcessAsync(exchange);
    }

    public override string ToString() => $"FromVariable[{variable}]";
}
