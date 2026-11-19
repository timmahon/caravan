using Caravan.Builder;
using Caravan.Components.DataSet;
using Caravan.Components.Mock;

namespace Caravan.Core.Tests;

public class DataSetTests : ContextTestBase
{
    [Fact]
    public async Task A_data_set_round_trips_through_a_route()
    {
        Context.Registry.Bind("foo", new SimpleDataSet(20));
        const string uri = "dataset:foo?initialDelay=0&produceDelay=0";
        await StartWithRoutes(b =>
        {
            b.From(uri).To("seda:middle");
            b.From("seda:middle").To(uri);
        });

        var endpoint = Context.GetEndpoint<DataSetEndpoint>(uri);
        await endpoint.AssertIsSatisfiedAsync();

        Assert.Equal(20, endpoint.ReceivedCounter);
        Assert.Equal(19L, endpoint.ReceivedExchanges[^1].Message.GetHeader(CaravanHeaders.DataSetIndex));
        Assert.Equal("<hello>world!</hello>", endpoint.ReceivedExchanges[0].Message.Body);
        await MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task List_data_set_cycles_its_bodies_and_sets_headers()
    {
        var dataSet = new ListDataSet(["a", "b", "c"]) { Size = 5 };
        dataSet.DefaultHeaders["source"] = "list";
        dataSet.OutputTransformer = e => e.Message.SetHeader("transformed", true);
        Context.Registry.Bind("list", dataSet);
        await StartWithRoutes(b => b.From("dataset:list?initialDelay=0").To("mock:out"));
        var mock = Mock("mock:out").ExpectedBodiesReceived("a", "b", "c", "a", "b");
        mock.AllMessages().Header("source").IsEqualTo("list");
        mock.AllMessages().Header("transformed").IsEqualTo(true);

        await mock.AssertIsSatisfiedAsync();
        Assert.Equal(1, dataSet.ReportCount);
    }

    [Fact]
    public async Task Producer_detects_messages_out_of_order_or_unexpected()
    {
        Context.Registry.Bind("ds", new ListDataSet(["x", "y"]));
        await Context.StartAsync();
        var endpoint = Context.GetEndpoint<DataSetEndpoint>("dataset:ds");

        await Template.SendBodyAndHeaderAsync("dataset:ds", "x", CaravanHeaders.DataSetIndex, 0L);
        await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAndHeaderAsync("dataset:ds", "y", CaravanHeaders.DataSetIndex, 5L));

        var e = await Assert.ThrowsAsync<MockAssertionException>(() => endpoint.AssertIsSatisfiedAsync(TimeSpan.FromMilliseconds(10)));
        Assert.Contains("does not match", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Producer_compares_bodies_and_index_modes()
    {
        Context.Registry.Bind("ds", new ListDataSet(["x", "y"]));
        await Context.StartAsync();

        await Template.SendBodyAsync("dataset:ds?dataSetIndex=lenient", "x");
        var wrong = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("dataset:ds?dataSetIndex=lenient", "z"));
        Assert.Contains("Body of message: 1", wrong.InnerException!.Message, StringComparison.Ordinal);

        var strict = await Assert.ThrowsAsync<CaravanExecutionException>(() => Template.SendBodyAsync("dataset:ds?dataSetIndex=strict", "x"));
        Assert.Contains("is missing", strict.InnerException!.Message, StringComparison.Ordinal);

        await Template.SendBodyAndHeaderAsync("dataset:ds?dataSetIndex=off", "x", CaravanHeaders.DataSetIndex, 99L);
        await Context.GetEndpoint<DataSetEndpoint>("dataset:ds?dataSetIndex=off").AssertIsNotSatisfiedAsync(TimeSpan.FromMilliseconds(1));
        Assert.Throws<ArgumentException>(() => Context.GetEndpoint("dataset:ds?dataSetIndex=sometimes"));
        Assert.Throws<NoSuchBeanException>(() => Context.GetEndpoint("dataset:missing"));
    }

    [Fact]
    public async Task Consumer_waits_for_min_rate_and_uses_the_context_clock()
    {
        var time = new TestTimeProvider();
        Context.TimeProvider = time;
        var dataSet = new ListDataSet([]);
        Context.Registry.Bind("growing", dataSet);
        await StartWithRoutes(b => b.From("dataset:growing?initialDelay=1s&produceDelay=0&minRate=2").To("mock:out"));
        var mock = Mock("mock:out").ExpectedBodiesReceived(1, 2);

        await time.WaitForTimersAsync(1);
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromSeconds(1));
        await time.AdvanceAndWaitForTimerAsync(TimeSpan.FromMilliseconds(10));
        Assert.Equal(0, mock.ReceivedCounter);
        dataSet.DefaultBodies.AddRange([1, 2]);
        time.Advance(TimeSpan.FromMilliseconds(10));

        await mock.AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Dataset_test_expects_the_messages_of_another_endpoint()
    {
        await Context.StartAsync();
        await Template.SendBodyAsync("seda:expected", "one");
        await Template.SendBodyAsync("seda:expected", "two\nthree");

        var ordered = Context.GetEndpoint<DataSetTestEndpoint>("dataset-test:seda:expected?timeout=50ms&split=true");
        await Template.SendBodyAsync("dataset-test:seda:expected?timeout=50ms&split=true", "one");
        await Template.SendBodyAsync("dataset-test:seda:expected?timeout=50ms&split=true", "two");
        await Template.SendBodyAsync("dataset-test:seda:expected?timeout=50ms&split=true", "three");

        await ordered.AssertIsSatisfiedAsync();
        Assert.Equal(3, ordered.ReceivedCounter);
    }

    [Fact]
    public async Task Dataset_test_in_any_order_and_failures()
    {
        await Context.StartAsync();
        await Template.SendBodyAsync("seda:expected2", "a");
        await Template.SendBodyAsync("seda:expected2", "b");

        var endpoint = Context.GetEndpoint<DataSetTestEndpoint>("dataset-test:seda:expected2?timeout=50ms&anyOrder=true");
        await Template.SendBodyAsync("dataset-test:seda:expected2?timeout=50ms&anyOrder=true", "b");
        await Template.SendBodyAsync("dataset-test:seda:expected2?timeout=50ms&anyOrder=true", "a");
        await endpoint.AssertIsSatisfiedAsync();

        await Template.SendBodyAsync("seda:expected3", "a");
        var wrong = Context.GetEndpoint<DataSetTestEndpoint>("dataset-test:seda:expected3?timeout=50ms");
        await Template.SendBodyAsync("dataset-test:seda:expected3?timeout=50ms", "b");
        await Assert.ThrowsAsync<MockAssertionException>(() => wrong.AssertIsSatisfiedAsync(TimeSpan.FromMilliseconds(10)));
    }
}
