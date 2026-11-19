namespace Caravan.Samples.Orders;

/// <summary>An order line read from the generated batch: <c>id:product:quantity</c>.</summary>
/// <param name="Id">The order id.</param>
/// <param name="Product">The product.</param>
/// <param name="Quantity">The quantity.</param>
public sealed record Order(int Id, string Product, int Quantity)
{
    /// <summary>Parses <c>id:product:quantity</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The order.</returns>
    public static Order Parse(string text)
    {
        var parts = text.Trim().Split(':');
        return parts.Length == 3 && int.TryParse(parts[0], out var id) && int.TryParse(parts[2], out var quantity)
            ? new Order(id, parts[1], quantity)
            : throw new FormatException($"Not an order: '{text}'");
    }
}
