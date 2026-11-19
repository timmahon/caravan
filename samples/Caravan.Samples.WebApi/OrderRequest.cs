namespace Caravan.Samples.WebApi;

/// <summary>An order placed through <c>POST /api/orders</c> (bound from JSON by the REST DSL).</summary>
public sealed class OrderRequest
{
    /// <summary>The product code.</summary>
    public string? Sku { get; set; }

    /// <summary>The quantity ordered.</summary>
    public int Quantity { get; set; }
}
