using System.Collections.Concurrent;

namespace Caravan.Samples.WebApi;

/// <summary>An in-memory inventory (a registry bean the routes call).</summary>
public sealed class Inventory
{
    private readonly ConcurrentDictionary<string, int> _stock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["widget"] = 100,
        ["gadget"] = 5,
    };

    /// <summary>The stock of a product, or null when it is unknown.</summary>
    /// <param name="sku">The product code.</param>
    /// <returns>The stock.</returns>
    public StockItem? Find(string sku) => _stock.TryGetValue(sku, out var quantity) ? new StockItem(sku, quantity) : null;

    /// <summary>Takes <see cref="OrderRequest.Quantity"/> items from the stock.</summary>
    /// <param name="order">The order.</param>
    /// <returns>The confirmation.</returns>
    /// <exception cref="InvalidOperationException">Not enough stock.</exception>
    public OrderConfirmation Reserve(OrderRequest order)
    {
        var sku = order.Sku ?? throw new ArgumentException("sku is required");
        while (true)
        {
            var current = _stock.GetValueOrDefault(sku);
            if (current < order.Quantity)
            {
                throw new InvalidOperationException($"Only {current} {sku} in stock");
            }

            if (_stock.TryUpdate(sku, current - order.Quantity, current))
            {
                return new OrderConfirmation(Guid.NewGuid().ToString("N")[..8], sku, order.Quantity);
            }
        }
    }
}
