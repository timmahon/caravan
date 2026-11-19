namespace Caravan.Samples.WebApi;

/// <summary>The reply to an accepted order.</summary>
/// <param name="OrderId">The generated order id.</param>
/// <param name="Sku">The product code.</param>
/// <param name="Quantity">The quantity reserved.</param>
public sealed record OrderConfirmation(string OrderId, string Sku, int Quantity);
