using Caravan.Components.Mock;
using Caravan.Samples.Orders;

namespace Caravan.Samples.Tests;

/// <summary>The orders sample's routes start and process one generated batch.</summary>
public class OrdersSampleTests
{
    [Fact]
    public async Task One_batch_is_split_routed_and_rejected_orders_are_handled()
    {
        await using var context = new CaravanContext();
        var properties = context.PropertiesComponent.InitialProperties;
        properties["orders.count"] = "1";
        properties["orders.delay"] = "0";
        properties["orders.priority"] = "mock:priority";
        properties["orders.standard"] = "mock:standard";
        properties["orders.rejected"] = "mock:rejected";
        properties["orders.deadLetter"] = "mock:dead";
        context.AddRoutes(new OrderRoutes());
        var priority = context.GetMockEndpoint("priority").ExpectedMessageCount(1);
        var standard = context.GetMockEndpoint("standard").ExpectedMessageCount(2);
        var rejected = context.GetMockEndpoint("rejected").ExpectedBodiesReceived("rejected 3: Unknown product: sprocket");

        await context.StartAsync();

        await MockEndpoint.AssertIsSatisfiedAsync(context);
        Assert.Equal(new Order(2, "gadget", 150), priority.ReceivedBodies[0]);
        Assert.Equal([1, 4], standard.GetReceivedBodies<Order>().Select(o => o!.Id).Order());
        Assert.Empty(context.GetMockEndpoint("dead").ReceivedExchanges);
    }

    [Fact]
    public void Orders_parse_and_bad_lines_fail()
    {
        Assert.Equal(new Order(1, "widget", 5), Order.Parse(" 1:widget:5 "));
        Assert.Throws<FormatException>(() => Order.Parse("nope"));
    }
}
