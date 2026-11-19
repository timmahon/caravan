using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// Runs a route's steps with its <c>onCompletion</c> blocks: each block is registered once per exchange on the unit of work
/// (<see cref="OnCompletionMode.AfterConsumer"/> blocks run when it is done); <see cref="OnCompletionMode.BeforeConsumer"/> blocks
/// run when the route's steps have finished, before the reply returns to the consumer.
/// </summary>
public sealed class OnCompletionProcessor : INavigableProcessor
{
    private readonly IReadOnlyList<(OnCompletionDefinition Definition, IProcessor Block)> _blocks;

    private OnCompletionProcessor(IProcessor pipeline, IReadOnlyList<(OnCompletionDefinition Definition, IProcessor Block)> blocks)
    {
        Pipeline = pipeline;
        _blocks = blocks;
    }

    /// <summary>The route's steps.</summary>
    public IProcessor Pipeline { get; }

    /// <inheritdoc />
    public IReadOnlyList<IProcessor> Next => [Pipeline, .. _blocks.Select(b => b.Block)];

    /// <summary>Wraps the route's <paramref name="pipeline"/> when the route has <c>onCompletion</c> blocks; returns it as is otherwise.</summary>
    public static IProcessor Wrap(RouteContext routeContext, IProcessor pipeline)
    {
        var blocks = new List<(OnCompletionDefinition, IProcessor)>();
        foreach (var definition in routeContext.Definition.OnCompletions.Where(d => !d.Disabled))
        {
            definition.Id ??= routeContext.Context.NextNodeId(definition.ShortName);
            var block = definition.CreateProcessor(routeContext);
            routeContext.AddService(block);
            blocks.Add((definition, block));
        }

        return blocks.Count == 0 ? pipeline : new OnCompletionProcessor(pipeline, blocks);
    }

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        List<OnCompletionSynchronization>? beforeConsumer = null;
        foreach (var (definition, block) in _blocks)
        {
            var synchronization = new OnCompletionSynchronization(definition, block);
            if (exchange.UnitOfWork is { } unitOfWork)
            {
                if (unitOfWork.ContainsSynchronization(synchronization))
                {
                    // Already registered by another route the exchange passed through: run once per exchange.
                    continue;
                }

                unitOfWork.AddSynchronization(synchronization);
            }
            else
            {
                exchange.AddOnCompletion(synchronization);
            }

            if (definition.Mode == OnCompletionMode.BeforeConsumer)
            {
                (beforeConsumer ??= []).Add(synchronization);
            }
        }

        try
        {
            await Pipeline.ProcessAsync(exchange).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            exchange.Exception = e;
        }

        if (beforeConsumer is not null)
        {
            foreach (var synchronization in beforeConsumer)
            {
                await synchronization.RunBeforeConsumerAsync(exchange).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"OnCompletion[{Pipeline}]";
}
