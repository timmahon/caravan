using System.Runtime.CompilerServices;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Processors;

/// <summary>
/// The <c>recipientList</c> EIP: evaluates <see cref="Expression"/> on the input exchange to get the recipients (URIs or endpoints:
/// a string split on <see cref="Delimiter"/>, a sequence, or an <see cref="IEndpoint"/>) and sends a correlated copy to each, like
/// <see cref="MulticastProcessor"/> (which describes failures, parallel processing and aggregation). Each branch carries
/// <see cref="ExchangePropertyKeys.RecipientListEndpoint"/>, <see cref="ExchangePropertyKeys.MulticastIndex"/> and
/// <see cref="ExchangePropertyKeys.MulticastComplete"/>.
/// </summary>
/// <remarks>
/// All recipients are resolved before anything is sent: an invalid one fails the exchange unless <see cref="IgnoreInvalidEndpoints"/>.
/// Producers are cached in a <see cref="ProducerCache"/>. Each send is wrapped with the route's error handler, so a failing recipient is
/// redelivered on its own.
/// </remarks>
public sealed class RecipientListProcessor : MulticastProcessor
{
    private readonly Func<IProcessor, IProcessor>? _wrap;

    /// <summary>Creates the processor.</summary>
    /// <param name="expression">Computes the recipients.</param>
    /// <param name="settings">The options; the default strategy of the EIP is <see cref="Aggregation.UseLatestAggregationStrategy"/>.</param>
    /// <param name="delimiter">Splits a string of recipients (default <c>,</c>); null or <c>false</c> to not split.</param>
    /// <param name="ignoreInvalidEndpoints">Skips recipients that cannot be resolved instead of failing.</param>
    /// <param name="cacheSize">Maximum cached producers; 0 uses the default, negative disables caching.</param>
    /// <param name="wrapRecipient">Wraps the sender of each recipient (with error handling).</param>
    public RecipientListProcessor(
        IExpression expression,
        MulticastSettings settings,
        string? delimiter = ",",
        bool ignoreInvalidEndpoints = false,
        int cacheSize = ProducerCache.DefaultCacheSize,
        Func<IProcessor, IProcessor>? wrapRecipient = null)
        : base([], settings)
    {
        Expression = expression;
        Delimiter = delimiter is null || delimiter.Equals("false", StringComparison.OrdinalIgnoreCase) ? null : delimiter;
        IgnoreInvalidEndpoints = ignoreInvalidEndpoints;
        ProducerCache = new ProducerCache(null, cacheSize);
        _wrap = wrapRecipient;
    }

    /// <summary>Computes the recipients.</summary>
    public IExpression Expression { get; }

    /// <summary>Splits a string of recipients; null when strings are not split.</summary>
    public string? Delimiter { get; }

    /// <summary>Whether recipients that cannot be resolved are skipped.</summary>
    public bool IgnoreInvalidEndpoints { get; }

    /// <summary>The producers used to send to the recipients.</summary>
    public ProducerCache ProducerCache { get; }

    /// <inheritdoc />
    public override string ToString() => $"RecipientList[{Expression}]";

    /// <summary>Waits for running branches, then stops the cached producers.</summary>
    protected override async Task DoStopAsync(CancellationToken cancellationToken)
    {
        await base.DoStopAsync(cancellationToken).ConfigureAwait(false);
        await ProducerCache.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private protected override async IAsyncEnumerable<MulticastBranch> CreateBranchesAsync(Exchange original, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var endpoints = new List<IEndpoint>();
        foreach (var recipient in SplitIterator.Create(Expression.Evaluate(original), Delimiter))
        {
            if (recipient is string text && string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            try
            {
                endpoints.Add(ProducerCache.ResolveEndpoint(original, recipient));
            }
            catch (Exception e) when (IgnoreInvalidEndpoints)
            {
                original.Context.LoggerFactory.CreateLogger<RecipientListProcessor>()
                    .LogDebug(e, "Ignoring invalid recipient {Recipient} for exchange {ExchangeId}", recipient, original.ExchangeId);
            }
        }

        foreach (var endpoint in endpoints)
        {
            var exchange = original.CreateCorrelatedCopy();
            exchange.SetProperty(ExchangePropertyKeys.RecipientListEndpoint, endpoint.EndpointUri);
            IProcessor sender = new RecipientSender(ProducerCache, endpoint);
            yield return new MulticastBranch(_wrap?.Invoke(sender) ?? sender, exchange, endpoints.Count);
        }
    }

    private sealed class RecipientSender(ProducerCache cache, IEndpoint endpoint) : IProcessor
    {
        public ValueTask ProcessAsync(Exchange exchange) => cache.SendAsync(endpoint, exchange);

        public override string ToString() => $"SendTo[{endpoint.EndpointUri}]";
    }
}
