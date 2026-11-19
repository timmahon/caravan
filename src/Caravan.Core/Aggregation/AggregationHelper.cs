namespace Caravan.Aggregation;

/// <summary>Helpers shared by the built-in aggregation strategies.</summary>
internal static class AggregationHelper
{
    /// <summary>Copies the failure of <paramref name="source"/> (exception and exhausted flag) to <paramref name="target"/>.</summary>
    public static void PropagateException(Exchange target, Exchange source)
    {
        if (ReferenceEquals(target, source) || source.Exception is null)
        {
            return;
        }

        target.Exception = source.Exception;
        target.IsRedeliveryExhausted = source.IsRedeliveryExhausted;
    }

    /// <summary>Gets (or creates) the list stored in <see cref="ExchangePropertyKeys.GroupedExchange"/> on <paramref name="exchange"/>.</summary>
    public static List<T> GetList<T>(Exchange exchange)
    {
        if (exchange.GetProperty(ExchangePropertyKeys.GroupedExchange) is List<T> list)
        {
            return list;
        }

        list = [];
        exchange.SetProperty(ExchangePropertyKeys.GroupedExchange, list);
        return list;
    }

    /// <summary>Moves the list stored in <see cref="ExchangePropertyKeys.GroupedExchange"/> to the body.</summary>
    public static void StoreListAsBody(Exchange exchange)
    {
        if (exchange.GetProperty(ExchangePropertyKeys.GroupedExchange) is { } list)
        {
            exchange.RemoveProperty(ExchangePropertyKeys.GroupedExchange);
            exchange.Message.Body = list;
        }
    }

    /// <summary>Creates an empty exchange used to hold grouped results (it must not be one of the grouped exchanges).</summary>
    public static Exchange CreateHolder(Exchange from) =>
        new(from.Context, from.Pattern)
        {
            FromEndpoint = from.FromEndpoint,
            FromRouteId = from.FromRouteId,
            CurrentRouteId = from.CurrentRouteId,
            CancellationToken = from.CancellationToken,
        };
}
