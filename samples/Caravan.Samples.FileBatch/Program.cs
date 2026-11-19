using Caravan.Hosting;
using Caravan.Samples.FileBatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Drop CSV files (customer,amount lines) into data/inbox; each poll's files become one report in data/outbox.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCaravan(o => o.Name = "file-batch").AddRoutes<BatchRoutes>();
await builder.Build().RunAsync();
