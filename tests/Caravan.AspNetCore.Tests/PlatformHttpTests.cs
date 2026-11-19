using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Caravan.Builder;
using Caravan.Components.Mock;

namespace Caravan.AspNetCore.Tests;

public class PlatformHttpTests
{
    [Fact]
    public async Task Get_maps_path_parameters_query_and_headers_to_message_headers()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/orders/{id}?httpMethodRestrict=GET")
            .RouteId("orders")
            .SetBody(Expr.Simple("id=${header.id} q=${header.q} x=${header.X-Trace} m=${header.CaravanHttpMethod} uri=${header.CaravanHttpUri} " +
                "path=${header.CaravanHttpPath} query=${header.CaravanHttpQuery} raw=${header.CaravanHttpRawQuery}")));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/42?q=a%20b");
        request.Headers.Add("X-Trace", "t1");
        using var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "id=42 q=a b x=t1 m=GET uri=/orders/42 path=/orders/42 query=q=a b raw=q=a%20b",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Method_restriction_answers_405_for_other_methods()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/only-get?httpMethodRestrict=GET").SetBody(Expr.Constant("ok")));

        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync("/only-get")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await app.Client.PostAsync("/only-get", new StringContent("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/other")).StatusCode);
    }

    [Fact]
    public async Task Without_restriction_every_method_is_served()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/any").SetBody(Expr.Header(CaravanHeaders.HttpMethod)));

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var response = await app.Client.SendAsync(new HttpRequestMessage(method, "/any"));
            Assert.Equal(method.Method, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Post_body_is_cached_and_readable_more_than_once()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/echo?httpMethodRestrict=POST")
            .SetHeader("first", e => e.Message.GetBody<string>())
            .SetBody(Expr.Simple("${header.first}|${body}|${header.Content-Type}")));

        using var response = await app.Client.PostAsync("/echo", new StringContent("hello", Encoding.UTF8, "text/plain"));

        Assert.Equal("hello|hello|text/plain; charset=utf-8", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Response_code_content_type_and_headers_come_from_the_message()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/created")
            .SetHeader(CaravanHeaders.HttpResponseCode, Expr.Constant(201))
            .SetHeader(CaravanHeaders.ContentType, Expr.Constant("application/json"))
            .SetHeader("Location", Expr.Constant("/orders/1"))
            .SetHeader("CaravanInternal", Expr.Constant("secret"))
            .SetBody(Expr.Constant("{\"id\":1}")));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/created") { Content = new StringContent("x") };
        request.Headers.Add("X-Echo", "kept");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token");
        using var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("/orders/1", response.Headers.Location!.ToString());
        Assert.Equal("{\"id\":1}", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("CaravanInternal"));
        Assert.False(response.Headers.Contains("CaravanHttpMethod"));
        Assert.False(response.Headers.Contains("Authorization"));
        Assert.Equal("kept", response.Headers.GetValues("X-Echo").Single());
    }

    [Fact]
    public async Task Request_headers_starting_with_Caravan_are_not_copied_to_the_message()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/inject")
            .SetBody(e => e.Message.GetHeader(CaravanHeaders.HttpResponseCode)?.ToString() ?? "none"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/inject");
        request.Headers.Add(CaravanHeaders.HttpResponseCode, "500");
        using var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("none", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Empty_body_answers_204()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/empty").SetBody(Expr.Constant(null)));

        using var response = await app.Client.GetAsync("/empty");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Bytes_streams_and_objects_are_written()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.From("platform-http:/bytes").SetBody(Expr.Constant(new byte[] { 1, 2, 3 }));
            b.From("platform-http:/number").SetBody(Expr.Constant(42));
        });

        Assert.Equal([1, 2, 3], await app.Client.GetByteArrayAsync("/bytes"));
        Assert.Equal("42", await app.Client.GetStringAsync("/number"));
    }

    [Fact]
    public async Task Large_stream_responses_are_streamed()
    {
        const int size = 8 * 1024 * 1024;
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/large")
            .Process(e => e.Message.Body = new MemoryStream(Enumerable.Range(0, size).Select(i => (byte)(i % 251)).ToArray())));

        using var response = await app.Client.GetAsync("/large", HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[64 * 1024];
        long total = 0;
        var ok = true;
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                ok &= buffer[i] == (byte)((total + i) % 251);
            }

            total += read;
        }

        Assert.Equal(size, total);
        Assert.True(ok);
    }

    [Fact]
    public async Task Exceptions_answer_500_with_the_exception_text()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/fail")
            .ThrowException(new InvalidOperationException("boom")));

        using var response = await app.Client.GetAsync("/fail");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("InvalidOperationException: boom", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mute_exception_answers_500_without_body()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/mute?muteException=true")
            .ThrowException(new InvalidOperationException("boom")));

        using var response = await app.Client.GetAsync("/mute");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Exceptions_can_be_mapped_to_status_codes()
    {
        await using var app = await TestApp.StartAsync(
            c => c.AddRoutes(RouteBuilder.Create(b => b.From("platform-http:/missing").ThrowException(new KeyNotFoundException("no such order")))),
            component: c => c.MapException<KeyNotFoundException>(404).MapException<ArgumentException>(400));

        using var response = await app.Client.GetAsync("/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Handled_exceptions_answer_what_the_route_sets()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.OnException<InvalidOperationException>(o => o.Handled(true)
                .SetHeader(CaravanHeaders.HttpResponseCode, Expr.Constant(409))
                .SetBody(Expr.Simple("conflict: ${exception.message}")));
            b.From("platform-http:/conflict").ThrowException(new InvalidOperationException("taken"));
        });

        using var response = await app.Client.GetAsync("/conflict");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict: taken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Stopping_a_route_removes_its_endpoint_and_starting_it_adds_it_back()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/toggle").RouteId("toggle").SetBody(Expr.Constant("on")));

        Assert.Equal("on", await app.Client.GetStringAsync("/toggle"));

        await app.Context.StopRouteAsync("toggle");
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/toggle")).StatusCode);

        await app.Context.StartRouteAsync("toggle");
        Assert.Equal("on", await app.Client.GetStringAsync("/toggle"));

        await app.Context.SuspendRouteAsync("toggle");
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/toggle")).StatusCode);
        await app.Context.ResumeRouteAsync("toggle");
        Assert.Equal("on", await app.Client.GetStringAsync("/toggle"));

        await app.Context.StopRouteAsync("toggle");
        Assert.True(await app.Context.RemoveRouteAsync("toggle"));
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/toggle")).StatusCode);
    }

    [Fact]
    public async Task Routes_added_at_runtime_are_served()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/first").SetBody(Expr.Constant("1")));

        await app.Context.AddRoutesAsync(RouteBuilder.Create(b => b.From("platform-http:/second").SetBody(Expr.Constant("2"))));

        Assert.Equal("2", await app.Client.GetStringAsync("/second"));
    }

    [Fact]
    public async Task Match_on_uri_prefix_serves_sub_paths()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/files?matchOnUriPrefix=true")
            .SetBody(Expr.Header(CaravanHeaders.HttpPath)));

        Assert.Equal("/a/b.txt", await app.Client.GetStringAsync("/files/a/b.txt"));
        Assert.Equal("/", await app.Client.GetStringAsync("/files"));
    }

    [Fact]
    public async Task Consumes_and_produces_are_enforced()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/typed?consumes=application/json&produces=application/json")
            .SetBody(Expr.Constant("{}")));

        using var wrongType = await app.Client.PostAsync("/typed", new StringContent("x", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/typed");
        request.Headers.Accept.ParseAdd("application/xml");
        using var notAcceptable = await app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotAcceptable, notAcceptable.StatusCode);

        using var ok = await app.Client.PostAsync("/typed", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/json", ok.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Form_fields_become_headers()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/form").SetBody(Expr.Simple("${header.name}:${body}")));

        using var response = await app.Client.PostAsync("/form", new FormUrlEncodedContent([new("name", "joe")]));

        Assert.Equal("joe:name=joe", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Two_routes_on_the_same_path_and_method_conflict()
    {
        await using var app = await TestApp.StartAsync(b => b.From("platform-http:/dup?httpMethodRestrict=GET").RouteId("dup1").SetBody(Expr.Constant("1")));

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => app.Context.AddRoutesAsync(
            RouteBuilder.Create(b => b.From("platform-http:/dup?httpMethodRestrict=GET,POST").RouteId("dup2").SetBody(Expr.Constant("2")))));

        Assert.Contains("already serves the same path and method", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal("1", await app.Client.GetStringAsync("/dup"));
    }

    [Fact]
    public async Task Same_path_with_different_methods_is_served_by_different_routes()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.From("platform-http:/split?httpMethodRestrict=GET").SetBody(Expr.Constant("get"));
            b.From("platform-http:/split?httpMethodRestrict=POST").SetBody(Expr.Constant("post"));
        });

        Assert.Equal("get", await app.Client.GetStringAsync("/split"));
        Assert.Equal("post", await (await app.Client.PostAsync("/split", null)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Exchanges_reach_further_endpoints_as_InOut()
    {
        await using var app = await TestApp.StartAsync(b =>
        {
            b.From("platform-http:/forward").To("direct:work").To("mock:done");
            b.From("direct:work").Transform(Expr.Simple("worked on ${body}"));
        });
        var mock = app.Context.GetEndpoint<MockEndpoint>("mock:done");
        mock.ExpectedBodiesReceived("worked on job");

        using var response = await app.Client.PostAsync("/forward", new StringContent("job"));

        Assert.Equal("worked on job", await response.Content.ReadAsStringAsync());
        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(ExchangePattern.InOut, mock.GetExchanges()[0].Pattern);
    }
}
