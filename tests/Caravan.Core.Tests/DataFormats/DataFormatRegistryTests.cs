using System.Text;
using Caravan.Builder;
using Caravan.Components.DataFormat;
using Caravan.DataFormat;
using Caravan.Spi;

namespace Caravan.Core.Tests;

public class DataFormatRegistryTests : ContextTestBase
{
    [Fact]
    public void Built_ins_resolve_to_new_instances_and_unknown_names_fail()
    {
        Assert.IsType<JsonDataFormat>(Context.ResolveDataFormat("json"));
        Assert.IsType<JsonDataFormat>(Context.ResolveDataFormat("jackson"));
        Assert.IsType<XmlDataFormat>(Context.ResolveDataFormat("jaxb"));
        Assert.IsType<GzipDeflaterDataFormat>(Context.ResolveDataFormat("gzip"));
        Assert.NotSame(Context.ResolveDataFormat("csv"), Context.ResolveDataFormat("csv"));
        Assert.Contains("zipDeflater", Context.DataFormats.Names);
        Assert.Equal("nope", Assert.Throws<NoSuchDataFormatException>(() => Context.ResolveDataFormat("nope")).Name);
    }

    [Fact]
    public async Task Context_instances_registry_beans_and_factories_take_precedence()
    {
        var shared = new UpperDataFormat();
        Context.DataFormats.Add("upper", shared);
        Context.Registry.Bind("json", new UpperDataFormat());
        Context.DataFormats.AddFactory("lower", () => new StringDataFormat());
        Context.PropertiesComponent.InitialProperties["fmt"] = "upper";

        Assert.Same(shared, Context.ResolveDataFormat("upper"));
        Assert.Same(shared, Context.ResolveDataFormat("{{fmt}}"));
        Assert.IsType<UpperDataFormat>(Context.ResolveDataFormat("json"));
        Assert.IsType<StringDataFormat>(Context.ResolveDataFormat("lower"));

        await StartWithRoutes(b => b.From("direct:in").Marshal("upper").ConvertBodyTo(typeof(string)));
        Assert.Equal("HELLO", await Template.RequestBodyAsync("direct:in", "hello"));
    }

    [Fact]
    public async Task Unknown_data_format_name_fails_route_creation()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.From("direct:in").Marshal("nope")));

        var e = await Assert.ThrowsAsync<FailedToCreateRouteException>(() => Context.StartAsync());

        Assert.IsType<NoSuchDataFormatException>(e.InnerException);
    }

    [Fact]
    public async Task Dataformat_component_marshals_and_unmarshals_with_options()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:marshal").To("dataformat:json:marshal?prettyPrint=true&contentTypeHeader=false").To("mock:json");
            b.From("direct:unmarshal").To($"dataformat:json:unmarshal?unmarshalType={typeof(Order).FullName}");
            b.From("direct:csv").To("dataformat:csv:unmarshal?delimiter=;&useMaps=true");
        });
        var mock = Mock("mock:json").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:marshal", new Order("c", 1));
        var order = await Template.RequestBodyAsync("direct:unmarshal", """{"id":"c","total":4}""");
        var csv = await Template.RequestBodyAsync("direct:csv", "a;b\n1;2");

        await mock.AssertIsSatisfiedAsync();
        Assert.Contains("\n", mock.ReceivedExchanges[0].Message.GetBody<string>(), StringComparison.Ordinal);
        Assert.False(mock.ReceivedExchanges[0].Message.HasHeader("Content-Type"));
        Assert.Equal(new Order("c", 4), order);
        Assert.Equal("2", Assert.IsType<List<Dictionary<string, string?>>>(csv)[0]["b"]);
        var endpoint = Context.GetEndpoint<DataFormatEndpoint>("dataformat:csv:unmarshal?delimiter=;&useMaps=true");
        Assert.False(endpoint.IsMarshal);
        Assert.Equal(';', ((CsvDataFormat)endpoint.DataFormat).Delimiter);
    }

    [Fact]
    public async Task Dataformat_component_rejects_bad_uris()
    {
        await Context.StartAsync();

        Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("dataformat:json"));
        Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("dataformat:json:convert"));
        Assert.Throws<ResolveEndpointFailedException>(() => Context.GetEndpoint("dataformat:json:marshal?bogus=1"));
        Assert.Throws<NoSuchDataFormatException>(() => Context.GetEndpoint("dataformat:nope:marshal"));
    }

    [Fact]
    public async Task Data_formats_that_are_services_start_with_the_route()
    {
        var format = new ServiceDataFormat();
        await StartWithRoutes(b => b.From("direct:in").Marshal(format));

        Assert.Equal(ServiceStatus.Started, format.Status);
        await Context.StopAsync();
        Assert.Equal(ServiceStatus.Stopped, format.Status);
    }

    private sealed class UpperDataFormat : IDataFormat
    {
        public async ValueTask MarshalAsync(Exchange exchange, object? graph, Stream output) =>
            await output.WriteAsync(Encoding.UTF8.GetBytes(graph!.ToString()!.ToUpperInvariant()));

        public ValueTask<object?> UnmarshalAsync(Exchange exchange, Stream input) => ValueTask.FromResult<object?>(null);
    }

    private sealed class ServiceDataFormat : Support.ServiceSupport, IDataFormat
    {
        public ValueTask MarshalAsync(Exchange exchange, object? graph, Stream output) => ValueTask.CompletedTask;

        public ValueTask<object?> UnmarshalAsync(Exchange exchange, Stream input) => ValueTask.FromResult<object?>(null);
    }
}
