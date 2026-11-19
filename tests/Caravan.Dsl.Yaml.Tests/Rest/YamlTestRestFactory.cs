using System.Collections.Concurrent;
using Caravan.Model.Rest;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Dsl.Yaml.Tests.Rest;

/// <summary>A REST consumer factory without transport: started services are callable by "METHOD path".</summary>
public sealed class YamlTestRestFactory : ComponentBase, IRestConsumerFactory
{
    private readonly ConcurrentDictionary<string, IProcessor> _services = new(StringComparer.Ordinal);

    public IConsumer CreateConsumer(
        ICaravanContext context,
        IProcessor processor,
        string method,
        string basePath,
        string? uriTemplate,
        string? consumes,
        string? produces,
        RestConfiguration configuration,
        IReadOnlyDictionary<string, string> parameters)
    {
        var key = method + " " + basePath + uriTemplate;
        return new Consumer(this, context.GetEndpoint("browse:" + Guid.NewGuid().ToString("N")), processor, key);
    }

    public async Task<object?> CallAsync(ICaravanContext context, string service, string who)
    {
        var exchange = new Exchange(context, ExchangePattern.InOut);
        exchange.Message.SetHeader("who", who);
        await _services[service].ProcessAsync(exchange);
        return exchange.Message.Body;
    }

    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) =>
        throw new NotSupportedException();

    private sealed class Consumer(YamlTestRestFactory factory, IEndpoint endpoint, IProcessor processor, string key) : ConsumerBase(endpoint, processor)
    {
        protected override Task DoStartAsync(CancellationToken cancellationToken)
        {
            factory._services[key] = Processor;
            return Task.CompletedTask;
        }

        protected override Task DoStopAsync(CancellationToken cancellationToken)
        {
            factory._services.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
