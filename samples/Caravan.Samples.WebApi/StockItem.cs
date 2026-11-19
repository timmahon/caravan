namespace Caravan.Samples.WebApi;

/// <summary>The stock of a product.</summary>
/// <param name="Sku">The product code.</param>
/// <param name="Quantity">The quantity in stock.</param>
public sealed record StockItem(string Sku, int Quantity);
