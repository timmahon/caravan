using System.Collections.Concurrent;
using Caravan.Model.Rest;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Core.Tests.Rest;

/// <summary>A transport-less REST consumer factory: started services are kept by "METHOD path" so tests can call them.</summary>
public sealed class TestRestComponent : ComponentBase, IRestConsumerFactory
{
    public ConcurrentDictionary<string, IProcessor> Services { get; } = new(StringComparer.Ordinal);

    public List<(string Method, string Path, IReadOnlyDictionary<string, string> Parameters)> Created { get; } = [];

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
        var path = (configuration.ContextPath ?? string.Empty).TrimEnd('/') + basePath + uriTemplate;
        lock (Created)
        {
            Created.Add((method, path, parameters));
        }

        return new TestRestConsumer(this, context.GetEndpoint("test-rest:" + method + " " + path), processor, method + " " + path);
    }

    public async Task<Exchange> CallAsync(ICaravanContext context, string service, object? body = null, IDictionary<string, object?>? headers = null)
    {
        var exchange = new Exchange(context, ExchangePattern.InOut);
        exchange.Message.Body = body;
        exchange.Message.SetHeader(CaravanHeaders.HttpMethod, service.Split(' ')[0]);
        foreach (var (name, value) in headers ?? new Dictionary<string, object?>())
        {
            exchange.Message.SetHeader(name, value);
        }

        await Services[service].ProcessAsync(exchange);
        return exchange;
    }

    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters) =>
        new TestRestEndpoint(uri, this);
}
