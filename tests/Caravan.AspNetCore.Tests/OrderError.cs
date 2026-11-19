namespace Caravan.AspNetCore.Tests;

public class OrderError
{
    public string? Reason { get; set; }

    public override string ToString() => "OrderError:" + Reason;
}
