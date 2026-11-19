using Caravan.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Caravan.Samples.Orders;
using Microsoft.Extensions.Hosting;

// The order pipeline on the Generic Host: Ctrl+C stops the context gracefully.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCaravan(o => o.Name = "orders").AddRoutes<OrderRoutes>();
await builder.Build().RunAsync();
