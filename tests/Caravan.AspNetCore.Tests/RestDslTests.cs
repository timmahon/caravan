using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Caravan.Builder;
using Caravan.Model.Rest;
using Caravan.Spi;

namespace Caravan.AspNetCore.Tests;

public class RestDslTests
{
    private static void OrderRoutes(RouteBuilder b)
    {
        b.From("direct:getOrder").Process(e => e.Message.Body = new Order { Id = int.Parse(e.Message.GetHeader<string>("id")!), Item = "book", Quantity = 1 });
        b.From("direct:createOrder").Process(e =>
        {
            var order = (Order)e.Message.Body!;
            order.Id = 7;
            e.Message.SetHeader(CaravanHeaders.HttpResponseCode, 201);
        });
    }

    [Fact]
    public async Task Json_binding_unmarshals_requests_into_the_type_and_marshals_responses()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.RestConfiguration(c => c.BindingMode(RestBindingMode.Json));
            b.Rest("/api/orders", r => r
                .Get("/{id}", v => v.Produces("application/json").OutType<Order>().To("direct:getOrder"))
                .Post(v => v.Type<Order>().OutType<Order>().To("direct:createOrder")));
            OrderRoutes(b);
        });

        var order = await app.Client.GetFromJsonAsync<JsonObject>("/api/orders/5");
        Assert.Equal(5, (int)order!["id"]!);
        Assert.Equal("book", (string?)order["item"]);

        using var created = await app.Client.PostAsJsonAsync("/api/orders", new { item = "pen", quantity = 3 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("application/json", created.Content.Headers.ContentType!.MediaType);
        var body = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        Assert.Equal(7, (int)body["id"]!);
        Assert.Equal("pen", (string?)body["item"]);
        Assert.Equal(3, (int)body["quantity"]!);
    }

    [Fact]
    public async Task Every_verb_is_served()
    {
        await using var app = await TestApp.StartAsync(b => b.Rest("/api/items", r => r
            .Get(v => v.Route(s => s.SetBody(Expr.Constant("get"))))
            .Post(v => v.Route(s => s.SetBody(Expr.Constant("post"))))
            .Put("/{id}", v => v.Route(s => s.SetBody(Expr.Simple("put ${header.id}"))))
            .Patch("/{id}", v => v.Route(s => s.SetBody(Expr.Simple("patch ${header.id}"))))
            .Delete("/{id}", v => v.Route(s => s.SetBody(Expr.Simple("delete ${header.id}"))))
            .Head(v => v.Route(s => s.SetHeader("X-Count", Expr.Constant("3")).SetBody(Expr.Constant("ignored"))))
            .Options(v => v.Route(s => s.SetHeader("Allow", Expr.Constant("GET, POST")).SetBody(Expr.Constant(null))))));

        async Task<string> Call(HttpMethod method, string path)
        {
            using var response = await app.Client.SendAsync(new HttpRequestMessage(method, path));
            Assert.True(response.IsSuccessStatusCode, $"{method} {path}: {response.StatusCode}");
            return await response.Content.ReadAsStringAsync();
        }

        Assert.Equal("get", await Call(HttpMethod.Get, "/api/items"));
        Assert.Equal("post", await Call(HttpMethod.Post, "/api/items"));
        Assert.Equal("put 1", await Call(HttpMethod.Put, "/api/items/1"));
        Assert.Equal("patch 2", await Call(HttpMethod.Patch, "/api/items/2"));
        Assert.Equal("delete 3", await Call(HttpMethod.Delete, "/api/items/3"));
        using var head = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/items"));
        Assert.Equal("3", head.Headers.GetValues("X-Count").Single());
        using var options = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/items"));
        Assert.Equal(HttpStatusCode.NoContent, options.StatusCode);
        Assert.Contains("POST", options.Content.Headers.Allow);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await app.Client.DeleteAsync("/api/items")).StatusCode);
    }

    [Fact]
    public async Task Xml_binding_uses_the_xml_data_format()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.RestConfiguration(c => c.BindingMode(RestBindingMode.Xml));
            b.Rest("/xml/orders", r => r
                .Get("/{id}", v => v.OutType<Order>().To("direct:getOrder"))
                .Post(v => v.Type<Order>().To("direct:createOrder")));
            OrderRoutes(b);
        });

        using var get = await app.Client.GetAsync("/xml/orders/4");
        Assert.Equal("application/xml", get.Content.Headers.ContentType!.MediaType);
        var xml = XDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal("Order", xml.Root!.Name.LocalName);
        Assert.Equal("4", xml.Root.Element("Id")!.Value);

        using var post = await app.Client.PostAsync("/xml/orders", new StringContent("<Order><Item>cup</Item><Quantity>2</Quantity></Order>", Encoding.UTF8, "application/xml"));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = XDocument.Parse(await post.Content.ReadAsStringAsync());
        Assert.Equal("7", created.Root!.Element("Id")!.Value);
        Assert.Equal("cup", created.Root.Element("Item")!.Value);
    }

    [Fact]
    public async Task Auto_binding_follows_content_type_and_accept()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.RestConfiguration(c => c.BindingMode(RestBindingMode.Auto));
            b.Rest("/auto", r => r.Post(v => v.Type<Order>().To("direct:createOrder")));
            OrderRoutes(b);
        });

        using var xmlRequest = new HttpRequestMessage(HttpMethod.Post, "/auto")
        {
            Content = new StringContent("<Order><Item>a</Item></Order>", Encoding.UTF8, "application/xml"),
        };
        using var xmlResponse = await app.Client.SendAsync(xmlRequest);
        Assert.Equal("application/xml", xmlResponse.Content.Headers.ContentType!.MediaType);
        Assert.Equal("a", XDocument.Parse(await xmlResponse.Content.ReadAsStringAsync()).Root!.Element("Item")!.Value);

        using var mixed = new HttpRequestMessage(HttpMethod.Post, "/auto")
        {
            Content = new StringContent("<Order><Item>b</Item></Order>", Encoding.UTF8, "application/xml"),
        };
        mixed.Headers.Accept.ParseAdd("application/json");
        using var jsonResponse = await app.Client.SendAsync(mixed);
        Assert.Equal("application/json", jsonResponse.Content.Headers.ContentType!.MediaType);
        Assert.Equal("b", (string?)JsonNode.Parse(await jsonResponse.Content.ReadAsStringAsync())!["item"]);

        using var json = await app.Client.PostAsJsonAsync("/auto", new { item = "c" });
        Assert.Equal("c", (string?)JsonNode.Parse(await json.Content.ReadAsStringAsync())!["item"]);
    }

    [Fact]
    public async Task Off_binding_passes_bodies_as_they_are()
    {
        await using var app = await TestApp.StartAsync(b => b.Rest("/off", r => r
            .Post(v => v.Type<Order>().Produces("text/plain").Route(s => s.SetBody(e => $"{e.Message.Body is IStreamCache}:{e.Message.GetBody<string>()}")))));

        using var response = await app.Client.PostAsync("/off", new StringContent("{\"item\":\"x\"}", Encoding.UTF8, "application/json"));

        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("True:{\"item\":\"x\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Untyped_json_binding_gives_dictionaries()
    {
        await using var app = await TestApp.StartAsync(b => b.Rest("/untyped", r => r
            .BindingMode(RestBindingMode.Json)
            .Post(v => v.Route(s => s.SetBody(Expr.Simple("${body[item]}"))))));

        using var response = await app.Client.PostAsJsonAsync("/untyped", new { item = "lamp" });

        Assert.Equal("lamp", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Client_request_validation_answers_415_406_and_400()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.RestConfiguration(c => c.BindingMode(RestBindingMode.Json).ClientRequestValidation());
            b.Rest("/validated", r => r
                .Consumes("application/json").Produces("application/json")
                .Post(v => v
                    .Type<Order>()
                    .Param("body", RestParamType.Body)
                    .Param("mode", RestParamType.Query, p => p.AllowableValues("fast", "slow"))
                    .Param("X-Tenant", RestParamType.Header)
                    .Route(s => s.SetBody(Expr.Simple("${header.mode}")))));
        });

        async Task<HttpResponseMessage> Send(string query, string? contentType, string body, string? accept = "application/json", string? tenant = "t")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/validated" + query);
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = contentType is null ? null : new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            if (accept is not null)
            {
                request.Headers.Accept.ParseAdd(accept);
            }

            if (tenant is not null)
            {
                request.Headers.Add("X-Tenant", tenant);
            }

            return await app.Client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Send("?mode=fast", "text/plain", "{}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotAcceptable, (await Send("?mode=fast", "application/json", "{}", accept: "application/xml")).StatusCode);
        var missing = await Send(string.Empty, "application/json", "{}");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("mode", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send("?mode=fast", "application/json", "{}", tenant: null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send("?mode=warp", "application/json", "{}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send("?mode=fast", "application/json", "  ")).StatusCode);
        var invalid = await Send("?mode=fast", "application/json", "{not json");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.StartsWith("Invalid request body", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var ok = await Send("?mode=fast", "application/json", "{\"item\":\"a\"}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("fast", await ok.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Without_validation_an_invalid_body_fails_with_500()
    {
        await using var app = await TestApp.StartAsync(b => b.Rest("/lenient", r => r
            .BindingMode(RestBindingMode.Json)
            .Post(v => v.Type<Order>().Route(s => s.SetBody(Expr.Constant("ok"))))));

        using var response = await app.Client.PostAsync("/lenient", new StringContent("{oops", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Skip_binding_on_error_code_leaves_error_bodies_alone()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.RestConfiguration(c => c.BindingMode(RestBindingMode.Json));
            b.Rest("/errors", r => r
                .Get("/skip", v => v.Route(s => s.SetHeader(CaravanHeaders.HttpResponseCode, Expr.Constant(404)).SetBody(Expr.Constant(new OrderError { Reason = "gone" }))))
                .Get("/bind", v => v.SkipBindingOnErrorCode(false)
                    .Route(s => s.SetHeader(CaravanHeaders.HttpResponseCode, Expr.Constant(404)).SetBody(Expr.Constant(new OrderError { Reason = "gone" })))));
        });

        using var skipped = await app.Client.GetAsync("/errors/skip");
        Assert.Equal(HttpStatusCode.NotFound, skipped.StatusCode);
        Assert.Equal("OrderError:gone", await skipped.Content.ReadAsStringAsync());

        using var bound = await app.Client.GetAsync("/errors/bind");
        Assert.Equal(HttpStatusCode.NotFound, bound.StatusCode);
        Assert.Equal("{\"reason\":\"gone\"}", await bound.Content.ReadAsStringAsync());
        Assert.Equal("application/json", bound.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Cors_headers_are_added_and_preflight_is_answered()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.RestConfiguration(c => c.EnableCors().CorsHeader("Access-Control-Allow-Origin", "https://example.com"));
            b.Rest("/cors", r => r.Get(v => v.Route(s => s.SetBody(Expr.Constant("hi")))).Put(v => v.Route(s => s.SetBody(Expr.Constant("put")))));
            b.Rest("/nocors", r => r.EnableCors(false).Get(v => v.Route(s => s.SetBody(Expr.Constant("hi")))));
        });

        using var get = await app.Client.GetAsync("/cors");
        Assert.Equal("https://example.com", get.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal(RestConfiguration.CorsAllowMethods, get.Headers.GetValues("Access-Control-Allow-Methods").Single());

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/cors");
        preflight.Headers.Add("Origin", "https://example.com");
        preflight.Headers.Add("Access-Control-Request-Method", "PUT");
        using var answer = await app.Client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("https://example.com", answer.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("3600", answer.Headers.GetValues("Access-Control-Max-Age").Single());

        using var none = await app.Client.GetAsync("/nocors");
        Assert.False(none.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/nocors"))).StatusCode);
    }

    [Fact]
    public async Task Context_path_prefixes_every_service()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.RestConfiguration(c => c.ContextPath("/rest/"));
            b.Rest("/hello", r => r.Get(v => v.Route(s => s.SetBody(Expr.Constant("world")))));
        });

        Assert.Equal("world", await app.Client.GetStringAsync("/rest/hello"));
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/hello")).StatusCode);
    }

    [Fact]
    public async Task Query_parameters_get_their_default_values()
    {
        await using var app = await TestApp.StartAsync(b => b.Rest("/paged", r => r.Get(v => v
            .Param("page", RestParamType.Query, p => p.DefaultValue("1").Required(false))
            .Route(s => s.SetBody(Expr.Simple("page ${header.page}"))))));

        Assert.Equal("page 1", await app.Client.GetStringAsync("/paged"));
        Assert.Equal("page 3", await app.Client.GetStringAsync("/paged?page=3"));
    }

    [Fact]
    public async Task Direct_routes_are_inlined_into_their_service()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.Rest("/inline", r => r.Get(v => v.RouteId("get-inline").To("direct:inline")).Get("/shared", v => v.To("direct:shared")).Post("/shared", v => v.To("direct:shared")));
            b.From("direct:inline").RouteId("inline-impl").SetBody(Expr.Constant("inlined"));
            b.From("direct:shared").RouteId("shared").SetBody(Expr.Constant("shared"));
        });

        Assert.Equal("inlined", await app.Client.GetStringAsync("/inline"));
        Assert.Equal("shared", await app.Client.GetStringAsync("/inline/shared"));
        var ids = app.Context.RouteDefinitions.Select(r => r.Id).ToList();
        Assert.Contains("get-inline", ids);
        Assert.DoesNotContain("inline-impl", ids);
        Assert.Contains("shared", ids);
        Assert.Single(app.Context.RouteDefinitions, r => r.FromUri == "rest:get:/inline");
    }

    [Fact]
    public async Task Inline_routes_can_be_turned_off()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.RestConfiguration(c => c.InlineRoutes(false));
            b.Rest("/linked", r => r.Get(v => v.RouteId("rest-linked").To("direct:linked")));
            b.From("direct:linked").RouteId("linked").SetBody(Expr.Constant("linked"));
        });

        Assert.Equal("linked", await app.Client.GetStringAsync("/linked"));
        Assert.Contains("linked", app.Context.RouteDefinitions.Select(r => r.Id));
    }

    [Fact]
    public async Task Rest_routes_can_be_stopped_and_started()
    {
        await using var app = await TestApp.StartAsync(b => b.Rest("/managed", r => r.Get(v => v.RouteId("managed").Route(s => s.SetBody(Expr.Constant("up"))))));

        await app.Context.StopRouteAsync("managed");
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/managed")).StatusCode);
        await app.Context.StartRouteAsync("managed");
        Assert.Equal("up", await app.Client.GetStringAsync("/managed"));
    }

    [Fact]
    public async Task Disabled_services_are_not_served()
    {
        await using var app = await TestApp.StartAsync(b => b.Rest("/disabled", r => r
            .Get(v => v.Disabled().Route(s => s.SetBody(Expr.Constant("x"))))
            .Post(v => v.Route(s => s.SetBody(Expr.Constant("y"))))));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await app.Client.GetAsync("/disabled")).StatusCode);
    }

    [Fact]
    public async Task Json_binding_lists_and_pretty_print_through_data_format_properties()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.RestConfiguration(c => c.BindingMode(RestBindingMode.Json).DataFormatProperty("json.out.prettyPrint", "true"));
            b.Rest("/batch", r => r.Post(v => v.Type(typeof(Order[])).Route(s => s.Process(e =>
                e.Message.Body = ((Order[])e.Message.Body!).Select(o => o.Item).ToList()))));
        });

        using var response = await app.Client.PostAsJsonAsync("/batch", new[] { new { item = "a" }, new { item = "b" } });

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains('\n', text);
        Assert.Equal(["a", "b"], JsonNode.Parse(text)!.AsArray().Select(n => (string?)n));
    }
}
