namespace Caravan.Samples.Orders;

/// <summary>An order names a product the catalog does not know (a business error: handled, not retried).</summary>
/// <param name="product">The product.</param>
public sealed class UnknownProductException(string product) : Exception($"Unknown product: {product}")
{
    /// <summary>The product.</summary>
    public string Product { get; } = product;
}
