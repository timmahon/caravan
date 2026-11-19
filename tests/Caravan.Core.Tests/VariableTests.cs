using Caravan.Builder;
using Caravan.Impl;
using static Caravan.Builder.Lang;

namespace Caravan.Core.Tests;

/// <summary>Camel 4 variables: exchange, route and global variables, the variable EIPs, Simple functions and variableSend/variableReceive.</summary>
public class VariableTests : ContextTestBase
{
    [Fact]
    public void Exchange_variables_are_read_and_written_by_name()
    {
        var exchange = new Exchange(Context);
        exchange.SetVariable("a", 1);
        Assert.Equal(1, exchange.GetVariable("a"));
        Assert.Equal("1", exchange.GetVariable<string>("a"));
        Assert.True(exchange.HasVariables);

        exchange.SetVariable("a", null);
        Assert.Null(exchange.GetVariable("a"));
        Assert.False(exchange.HasVariables);
        Assert.Null(exchange.GetVariable("missing"));
    }

    [Fact]
    public void Copies_carry_the_variables_and_do_not_share_them()
    {
        var exchange = new Exchange(Context);
        exchange.SetVariable("a", "x");
        var copy = exchange.Copy();
        var correlated = exchange.CreateCorrelatedCopy();
        copy.SetVariable("a", "changed");

        Assert.Equal("x", exchange.GetVariable("a"));
        Assert.Equal("changed", copy.GetVariable("a"));
        Assert.Equal("x", correlated.GetVariable("a"));

        var target = new Exchange(Context);
        target.CopyResultsFrom(copy);
        Assert.Equal("changed", target.GetVariable("a"));
    }

    [Fact]
    public void Global_variables_are_shared_by_exchanges_and_the_context()
    {
        var first = new Exchange(Context);
        first.SetVariable("global:counter", 5);

        Assert.Equal(5, new Exchange(Context).GetVariable("global:counter"));
        Assert.Equal(5, Context.GetVariable("counter"));
        Assert.Equal(5, Context.GetVariable("global:counter"));
        Context.SetVariable("other", "o");
        Assert.Equal("o", Context.VariableRepositories.Global.GetVariable("other"));
        Assert.Null(first.GetVariable("counter"));
    }

    [Fact]
    public void Route_variables_belong_to_the_current_route_and_can_be_read_by_id_from_elsewhere()
    {
        var exchange = new Exchange(Context) { CurrentRouteId = "a" };
        exchange.SetVariable("route:count", 1);
        Assert.Equal(1, exchange.GetVariable("route:count"));
        Assert.Equal(1, Context.VariableRepositories.Route.GetVariable("a:count"));

        var other = new Exchange(Context) { CurrentRouteId = "b" };
        Assert.Null(other.GetVariable("route:count"));
        Assert.Equal(1, other.GetVariable("route:a:count"));

        var outside = new Exchange(Context);
        Assert.Throws<ArgumentException>(() => outside.GetVariable("route:count"));
    }

    [Fact]
    public void Unknown_repository_prefixes_fail_and_custom_repositories_are_found_by_id()
    {
        var exchange = new Exchange(Context);
        var e = Assert.Throws<ArgumentException>(() => exchange.SetVariable("nope:x", 1));
        Assert.Contains("VariableRepository with id: nope does not exist", e.Message);

        var custom = new InMemoryVariableRepository("tenant");
        Context.VariableRepositories.Add(custom);
        exchange.SetVariable("tenant:x", 1);
        Assert.Equal(1, custom.GetVariable("x"));

        var bound = new InMemoryVariableRepository("bound");
        Context.Registry.Bind("bound", bound);
        exchange.SetVariable("bound:y", 2);
        Assert.Equal(2, bound.GetVariable("y"));
    }

    [Fact]
    public void Remove_variable_supports_names_wildcards_and_repositories()
    {
        var exchange = new Exchange(Context);
        exchange.SetVariable("a1", 1);
        exchange.SetVariable("a2", 2);
        exchange.SetVariable("b", 3);
        exchange.SetVariable("global:g1", 1);
        exchange.SetVariable("global:g2", 2);

        Assert.True(exchange.RemoveVariable("a*"));
        Assert.Equal(["b"], exchange.Variables.Keys);
        Assert.False(exchange.RemoveVariable("a*"));
        Assert.True(exchange.RemoveVariable("global:*"));
        Assert.Empty(Context.VariableRepositories.Global.Variables);
        Assert.True(exchange.RemoveVariable("*"));
        Assert.False(exchange.HasVariables);
    }

    [Fact]
    public async Task Variable_eips_set_remove_and_convert_variables()
    {
        await StartWithRoutes(b => b.From("direct:start").RouteId("vars")
            .SetVariable("a", Simple("${body}"))
            .SetVariables(("b", Constant("2")), ("c", Simple("${variable.a}-${variable.b}")))
            .SetVariable("global:last", Simple("${body}"))
            .SetVariable("route:seen", Constant(true))
            .ConvertVariableTo("b", typeof(int))
            .ConvertVariableTo("a", typeof(int), toName: "n", mandatory: false)
            .SetHeader("vars", Simple("${variables.size}"))
            .RemoveVariable("c")
            .To("mock:result"));

        await Template.SendBodyAsync("direct:start", "x");

        var exchange = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.Equal("x", exchange.GetVariable("a"));
        Assert.Equal(2, exchange.GetVariable("b"));
        Assert.Null(exchange.GetVariable("n"));
        Assert.Null(exchange.GetVariable("c"));
        Assert.Equal(3, exchange.Message.GetHeader("vars"));
        Assert.Equal("x", Context.GetVariable("last"));
        Assert.Equal(true, Context.VariableRepositories.Route.GetVariable("vars:seen"));
    }

    [Fact]
    public async Task Mandatory_conversion_of_a_variable_fails_the_exchange()
    {
        await StartWithRoutes(b => b.From("direct:start").SetVariable("a", Constant("abc")).ConvertVariableTo("a", typeof(int)).To("mock:result"));

        var exchange = await Template.SendAsync("direct:start", e => e.Message.Body = "x");

        Assert.NotNull(exchange.Exception);
        Assert.Empty(Mock("result").ReceivedExchanges);
    }

    [Fact]
    public async Task Simple_functions_and_the_variable_language_read_variables()
    {
        Context.SetVariable("greeting", "hi");
        await StartWithRoutes(b => b.From("direct:start")
            .SetVariable("order", Constant(new Order("7", 3m)))
            .SetVariable("when", Constant(DateTimeOffset.FromUnixTimeMilliseconds(0)))
            .SetBody(Simple("${variable.order.Total}|${variable:order.Id}|${variable[order].Id}|${variableAs(order,Order).Id}|${variable.global:greeting}|${date:variable.when:yyyy}"))
            .SetHeader("lang", Lang.Language("variable", "order"))
            .SetHeader("expr", Lang.Variable("global:greeting"))
            .Filter(Simple("${variable.order.Id} == '7'"), f => f.To("mock:result")));

        await Template.SendBodyAsync("direct:start", "x");

        var exchange = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.Equal("3|7|7|7|hi|1970", exchange.Message.Body);
        Assert.IsType<Order>(exchange.Message.GetHeader("lang"));
        Assert.Equal("hi", exchange.Message.GetHeader("expr"));
    }

    [Fact]
    public async Task Variables_reach_split_parts_and_seda_copies()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").SetVariable("tag", Constant("t")).Split(Expr.Tokenize(","), s => s.To("mock:part")).To("seda:next");
            b.From("seda:next").SetBody(Simple("${variable.tag}")).To("mock:seda");
        });
        Mock("seda").ExpectedBodiesReceived("t");

        await Template.SendBodyAsync("direct:start", "a,b");

        Assert.All(Mock("part").ReceivedExchanges, e => Assert.Equal("t", e.GetVariable("tag")));
        await Mock("seda").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task ToV_sends_a_variable_and_receives_the_reply_into_a_variable()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .SetVariable("request", Constant("question"))
                .ToV("direct:service", "request", "reply")
                .To("mock:result");
            b.From("direct:service").To("mock:service").SetBody(Simple("answer to ${body}")).SetHeader("status", Constant("ok"));
        });

        await Template.SendBodyAndHeaderAsync("direct:start", "original", "h", "keep");

        Assert.Equal(["question"], Mock("service").ReceivedBodies);
        Assert.Equal("keep", Mock("service").ReceivedExchanges[0].Message.GetHeader("h"));
        var exchange = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.Equal("original", exchange.Message.Body);
        Assert.Null(exchange.Message.GetHeader("status"));
        Assert.Equal("keep", exchange.Message.GetHeader("h"));
        Assert.Equal("answer to question", exchange.GetVariable("reply"));
        Assert.Equal("ok", exchange.GetVariable("reply.header.status"));
        Assert.Equal("ok", Expr.Simple("${variable.reply.header.status}").Evaluate(exchange));
    }

    [Fact]
    public async Task Variable_send_alone_restores_the_body_after_an_in_only_send_and_keeps_an_in_out_reply()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:inOnly").SetVariable("v", Constant("from-var")).ToV("mock:sent", "v", null).To("mock:inOnly");
            b.From("direct:inOut").SetVariable("v", Constant("from-var")).ToV("direct:echo", "v", null, ExchangePattern.InOut).To("mock:inOut");
            b.From("direct:echo").SetBody(Simple("echo ${body}"));
        });

        await Template.SendBodyAsync("direct:inOnly", "body");
        await Template.SendBodyAsync("direct:inOut", "body");

        Assert.Equal(["from-var"], Mock("sent").ReceivedBodies);
        Assert.Equal(["body"], Mock("inOnly").ReceivedBodies);
        Assert.Equal(["echo from-var"], Mock("inOut").ReceivedBodies);
    }

    [Fact]
    public async Task A_failed_send_with_variables_restores_the_message_and_stores_nothing()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start").ErrorHandler(new Model.NoErrorHandlerDefinition())
                .DoTry(t => t.ToV("direct:fail", null, "reply"), c => c.DoCatch<InvalidOperationException>(x => x.To("mock:caught")));
            b.From("direct:fail").SetBody(Constant("changed")).Process(_ => throw new InvalidOperationException("boom"));
        });

        await Template.SendBodyAsync("direct:start", "body");

        var exchange = Assert.Single(Mock("caught").ReceivedExchanges);
        Assert.Equal("body", exchange.Message.Body);
        Assert.Null(exchange.GetVariable("reply"));
    }

    [Fact]
    public async Task ToD_enrich_poll_enrich_and_poll_support_variables()
    {
        await StartWithRoutes(b =>
        {
            b.From("direct:start")
                .SetVariable("name", Constant("world"))
                .ToD("direct:${header.target}", variableSend: "name", variableReceive: "d")
                .Enrich("direct:hello", variableSend: "name", variableReceive: "e")
                .PollEnrich("seda:box", timeout: TimeSpan.FromSeconds(5), variableReceive: "p")
                .Poll("seda:box2", TimeSpan.FromSeconds(5), variableReceive: "q")
                .To("mock:result");
            b.From("direct:hello").SetBody(Simple("hello ${body}"));
        });
        await Template.SendBodyAndHeaderAsync("seda:box", "polled", "k", "v");
        await Template.SendBodyAsync("seda:box2", "polled2");

        await Template.SendBodyAndHeaderAsync("direct:start", "body", "target", "hello");

        var exchange = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.Equal("body", exchange.Message.Body);
        Assert.Equal("hello world", exchange.GetVariable("d"));
        Assert.Equal("hello world", exchange.GetVariable("e"));
        Assert.Equal("polled", exchange.GetVariable("p"));
        Assert.Equal("v", exchange.GetVariable("p.header.k"));
        Assert.Equal("polled2", exchange.GetVariable("q"));
    }

    [Fact]
    public async Task Poll_eip_replaces_the_message_and_gives_a_null_body_on_timeout()
    {
        await StartWithRoutes(b => b.From("direct:start").Poll("seda:${header.queue}", TimeSpan.Zero).To("mock:result"));
        await Template.SendBodyAndHeaderAsync("seda:in", "polled", "k", "v");

        await Template.SendBodyAndHeaderAsync("direct:start", "body", "queue", "in");
        await Template.SendBodyAndHeaderAsync("direct:start", "body", "queue", "in");

        Assert.Equal(["polled", null], Mock("result").ReceivedBodies);
        Assert.Equal("v", Mock("result").ReceivedExchanges[0].Message.GetHeader("k"));
        var definition = new Model.PollDefinition(Expr.Constant("seda:x"));
        Assert.Equal(TimeSpan.FromSeconds(20), definition.Timeout);
    }

    [Fact]
    public async Task FromV_puts_the_consumed_message_in_a_variable()
    {
        Context.AddRoutes(RouteBuilder.Create(b => b.FromV("direct:start", "input")
            .SetBody(Simple("got ${variable.input} with ${variable.input.header.h}"))
            .To("mock:result")));
        await Context.StartAsync();

        await Template.SendBodyAndHeaderAsync("direct:start", "msg", "h", "hv");

        var exchange = Assert.Single(Mock("result").ReceivedExchanges);
        Assert.Equal("got msg with hv", exchange.Message.Body);
        Assert.Null(exchange.Message.GetHeader("h"));
    }

    [Fact]
    public void Message_timestamp_reads_the_timestamp_header()
    {
        var exchange = new Exchange(Context);
        Assert.Equal(0L, Expr.Simple("${messageTimestamp}").Evaluate(exchange));
        Assert.Null(Expr.Simple("${date:messageTimestamp}").Evaluate(exchange));

        exchange.Message.SetHeader(CaravanHeaders.MessageTimestamp, 86_400_000L);
        Assert.Equal(86_400_000L, exchange.Message.MessageTimestamp);
        Assert.Equal(86_400_000L, Expr.Simple("${messageTimestamp}").Evaluate(exchange));
        Assert.Equal("1970-01-02", Expr.Simple("${date:messageTimestamp:yyyy-MM-dd}").Evaluate(exchange));

        exchange.Message.SetHeader(CaravanHeaders.MessageTimestamp, DateTimeOffset.FromUnixTimeMilliseconds(5));
        Assert.Equal(5L, exchange.Message.MessageTimestamp);
    }

    [Fact]
    public async Task Timer_messages_have_a_timestamp_with_include_metadata()
    {
        await StartWithRoutes(b => b.From("timer:t?repeatCount=1&delay=0&includeMetadata=true")
            .SetBody(Simple("${messageTimestamp}"))
            .To("mock:result"));
        Mock("result").ExpectedMessageCount(1);

        await Mock("result").AssertIsSatisfiedAsync();

        Assert.True(Mock("result").GetReceivedBodies<long>()[0] > 0);
    }
}
