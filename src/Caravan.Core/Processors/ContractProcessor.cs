using Caravan.DataTypes;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// Applies a route's data type contract around its steps (Camel's <c>ContractAdvice</c>): the message is transformed to the input type (and
/// validated) before the first step, and transformed to the output type (and validated) after the last one unless the exchange failed.
/// </summary>
internal sealed class ContractProcessor(DataTypeContract? input, DataTypeContract? output, IProcessor next) : INavigableProcessor
{
    public IReadOnlyList<IProcessor> Next => [next];

    public static IProcessor Wrap(Model.RouteDefinition route, IProcessor pipeline) =>
        route.InputType is null && route.OutputType is null ? pipeline : new ContractProcessor(route.InputType, route.OutputType, pipeline);

    public async ValueTask ProcessAsync(Exchange exchange)
    {
        if (input is not null)
        {
            await DataTypeTransformation.TransformAsync(exchange.Message, null, input.Type).ConfigureAwait(false);
            if (input.Validate)
            {
                await DataTypeTransformation.ValidateAsync(exchange.Message, input.Type).ConfigureAwait(false);
            }
        }

        await next.ProcessAsync(exchange).ConfigureAwait(false);
        if (output is not null && !exchange.IsFailed && !exchange.IsRollbackOnly)
        {
            await DataTypeTransformation.TransformAsync(exchange.Message, null, output.Type).ConfigureAwait(false);
            if (output.Validate)
            {
                await DataTypeTransformation.ValidateAsync(exchange.Message, output.Type).ConfigureAwait(false);
            }
        }
    }

    public override string ToString() => $"Contract[{input?.Type} -> {output?.Type}]";
}
