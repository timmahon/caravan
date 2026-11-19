using Caravan.Builder;
using Caravan.Model.Rest;
using static Caravan.Builder.Lang;

namespace Caravan.Samples.WebApi;

/// <summary>The C# half of the API: <c>GET /api/stock/{sku}</c>, served by ASP.NET Core through the REST DSL.</summary>
public sealed class InventoryRoutes : Caravan.Builder.RouteBuilder
{
    /// <inheritdoc />
    public override void Configure()
    {
        RestConfiguration(c => c.BindingMode(RestBindingMode.Json).ApiProperty("api.title", "Caravan sample API"));

        Rest("/api/stock", r => r
            .Tag("stock")
            .Get("/{sku}", v => v
                .OutType<StockItem>()
                .Param("sku", RestParamType.Path)
                .ResponseMessage(404, "Unknown product")
                .To("direct:getStock")));

        From("direct:getStock")
            .RouteId("get-stock")
            .SetBody(e => e.Context.Registry.Lookup<Inventory>("inventory")!.Find(e.Message.GetHeader<string>("sku")!))
            .Filter(Simple("${body} == null"), f => f
                .SetHeader(CaravanHeaders.HttpResponseCode, Constant(404))
                .SetBody(Constant(new { error = "unknown product" })));
    }
}
