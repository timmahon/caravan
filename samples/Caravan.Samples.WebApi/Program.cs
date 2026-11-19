using Caravan.Samples.WebApi;

// GET /api/stock/widget, POST /api/orders {"sku":"widget","quantity":2}, GET /openapi.json, /health, /caravan/health, /caravan/management/...
await WebApiSample.Create(args).RunAsync();
