using System.Text.RegularExpressions;
using Caravan.Components.Mock;
using Caravan.Support;

namespace Caravan.Components.DataSet;

/// <summary>
/// A <c>dataset-test</c> endpoint (Camel's <c>DataSetTestEndpoint</c>): a <see cref="MockEndpoint"/> whose expected bodies are read from
/// <see cref="ExpectedMessageEndpointUri"/> when it starts.
/// </summary>
public sealed class DataSetTestEndpoint : MockEndpoint
{
    internal DataSetTestEndpoint(string uri, DataSetTestComponent component, string expectedUri)
        : base(uri, component, expectedUri)
    {
        ExpectedMessageEndpointUri = expectedUri;
    }

    /// <summary>The URI the expected messages are read from.</summary>
    public string ExpectedMessageEndpointUri { get; }

    /// <summary>How long to wait for each expected message (default 2s, as Camel); reading stops at the first wait that times out.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Accept the messages in any order (default false).</summary>
    public bool AnyOrder { get; set; }

    /// <summary>Split each expected body (as text) on <see cref="Delimiter"/>, each part being an expected message (default false).</summary>
    public bool Split { get; set; }

    /// <summary>The regular expression splitting expected bodies with <see cref="Split"/> (default line breaks).</summary>
    public string Delimiter { get; set; } = "\\n|\\r";

    /// <summary>Reads the expected messages and sets the expectation.</summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task completed when the expectation is set.</returns>
    protected override async Task DoStartAsync(CancellationToken cancellationToken)
    {
        var endpoint = Context.GetEndpoint(ExpectedMessageEndpointUri);
        var consumer = PollingConsumerFactory.Create(endpoint);
        var bodies = new List<object?>();
        await consumer.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (await consumer.ReceiveAsync(Timeout, cancellationToken).ConfigureAwait(false) is { } exchange)
            {
                var body = exchange.Message.Body;
                if (Split && ObjectHelper.ToText(exchange, body) is { } text)
                {
                    bodies.AddRange(Regex.Split(text, Delimiter).Where(p => p.Length > 0));
                }
                else
                {
                    bodies.Add(body);
                }
            }
        }
        finally
        {
            await consumer.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        if (AnyOrder)
        {
            ExpectedBodiesReceivedInAnyOrder([.. bodies]);
        }
        else
        {
            ExpectedBodiesReceived([.. bodies]);
        }
    }
}
