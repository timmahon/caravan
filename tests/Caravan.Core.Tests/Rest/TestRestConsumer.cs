using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Core.Tests.Rest;

public sealed class TestRestConsumer(TestRestComponent component, IEndpoint endpoint, IProcessor processor, string key) : ConsumerBase(endpoint, processor)
{
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        if (!component.Services.TryAdd(key, Processor))
        {
            throw new InvalidOperationException("Duplicate service " + key);
        }

        return Task.CompletedTask;
    }

    protected override Task DoStopAsync(CancellationToken cancellationToken)
    {
        component.Services.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
