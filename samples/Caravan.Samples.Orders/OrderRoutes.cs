using Caravan.Builder;
using static Caravan.Builder.Lang;

namespace Caravan.Samples.Orders;

/// <summary>
/// The order pipeline: a timer produces a batch of orders, <c>split</c> handles each one, <c>choice</c> routes it to a priority or a standard
/// <c>seda</c> queue, consumers log them. Unknown products are handled by an <c>onException</c>; other failures are retried, then moved to a
/// dead letter queue. Endpoints come from properties (<c>orders.*</c> in appsettings.json) so tests can replace them.
/// </summary>
public sealed class OrderRoutes : RouteBuilder
{
    /// <summary>The products the catalog knows.</summary>
    public static readonly IReadOnlySet<string> Products = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "widget", "gadget", "gizmo" };

    /// <inheritdoc />
    public override void Configure()
    {
        ErrorHandler(DeadLetterChannel("{{orders.deadLetter:log:dead?level=ERROR}}").MaximumRedeliveries(2).RedeliveryDelay(TimeSpan.FromMilliseconds(10)));

        OnException<UnknownProductException>(o => o
            .Handled(true)
            .SetBody(Simple("rejected ${body.Id}: ${exception.message}"))
            .To("{{orders.rejected:log:rejected?level=WARN}}"));

        From("timer:orders?period={{orders.period:5000}}&repeatCount={{orders.count:0}}&delay={{orders.delay:1000}}")
            .RouteId("generate-orders")
            .SetBody(Constant("1:widget:5, 2:gadget:150, 3:sprocket:1, 4:gizmo:20"))
            .Split(Expr.Tokenize(","), split => split.To("direct:order"));

        From("direct:order")
            .RouteId("route-order")
            .Transform((string text) => Order.Parse(text))
            .Process(e =>
            {
                var order = e.Message.GetBody<Order>()!;
                if (!Products.Contains(order.Product))
                {
                    throw new UnknownProductException(order.Product);
                }
            })
            .Choice(c => c
                .When((Order o) => o.Quantity >= 100, r => r.To("seda:priority"))
                .Otherwise(r => r.To("seda:standard")));

        From("seda:priority")
            .RouteId("priority-orders")
            .Log("PRIORITY order ${body.Id}: ${body.Quantity} x ${body.Product}")
            .To("{{orders.priority:log:priority}}");

        From("seda:standard?concurrentConsumers=2")
            .RouteId("standard-orders")
            .Log("Standard order ${body.Id}: ${body.Quantity} x ${body.Product}")
            .To("{{orders.standard:log:standard}}");
    }
}
