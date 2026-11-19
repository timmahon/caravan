using System.Collections;
using Caravan.Language.Simple;

namespace Caravan.Core.Tests.Languages;

[Collection(SimpleGlobalStateCollection.Name)]
public class SimpleFunctionTests : SimpleTestBase
{
    [Fact]
    public void Text_without_functions_is_returned_as_is()
    {
        AssertExpression("Hello World", "Hello World");
        AssertExpression(string.Empty, string.Empty);
        AssertExpression("price: $5 and {braces}", "price: $5 and {braces}");
    }

    [Fact]
    public void A_lone_function_keeps_its_type_and_mixed_text_is_a_string()
    {
        Exchange.Message.Body = 42;

        Assert.Equal(42, Eval("${body}"));
        Assert.Equal("42!", Eval("${body}!"));
        Assert.Equal("42 123", Eval("${body} ${header.bar}"));
        Assert.Equal(42, Eval("$simple{body}"));
        Assert.Equal("v=42", Eval("v=$simple{body}"));
    }

    [Fact]
    public void Tokens_of_the_former_interim_template_keep_working()
    {
        var e = new Exchange(Context);
        e.Message.Body = "B";
        e.Message.SetHeader("h", 1);
        e.SetProperty("p", "P");
        e.CurrentRouteId = "r1";

        var text = Expr.Simple("${body}|${in.body}|${header.h}|${headers.H}|${header[h]}|${exchangeProperty.p}|${exchangeProperty['p']}|${routeId}|${header.none}|")
            .Evaluate(e);

        Assert.Equal("B|B|1|1|1|P|P|r1||", text);
        Assert.Equal(e.ExchangeId, Expr.Simple("${exchangeId}").Evaluate(e));
        Assert.Equal(e.Message.MessageId, Expr.Simple("${id}").Evaluate(e));
        Assert.Equal("mock:a?x=1", Expr.Simple("mock:a?x=${header.h}").Evaluate(e));
    }

    [Fact]
    public void Body_functions()
    {
        AssertExpression("${body}", "<hello id='m123'>world!</hello>");
        AssertExpression("${in.body}", "<hello id='m123'>world!</hello>");
        AssertExpression("${bodyType}", typeof(string));
        Exchange.Message.Body = "a\nb\r\nc";
        AssertExpression("${bodyOneLine}", "abc");
        Exchange.Message.Body = null;
        AssertExpression("${body}", null);
        AssertExpression("${bodyType}", null);
        AssertExpression("[${body}]", "[]");
    }

    [Fact]
    public void BodyAs_converts_and_mandatoryBodyAs_fails_when_it_cannot()
    {
        Exchange.Message.Body = "123";

        AssertExpression("${bodyAs(int)}", 123);
        AssertExpression("${bodyAs(Integer)}", 123);
        AssertExpression("${bodyAs(java.lang.Long)}", 123L);
        AssertExpression("${bodyAs('System.Decimal')}", 123m);
        AssertExpression("${bodyAs(String).Length}", 3);
        AssertExpression("${mandatoryBodyAs(int)}", 123);
        AssertExpression("${bodyAs(byte[]).Length}", 3);

        Exchange.Message.Body = "abc";
        AssertExpression("${bodyAs(int)}", null);
        Assert.Throws<InvalidPayloadException>(() => Eval("${mandatoryBodyAs(int)}"));
        Exchange.Message.Body = null;
        Assert.Throws<InvalidPayloadException>(() => Eval("${mandatoryBodyAs(String)}"));
    }

    [Fact]
    public void Header_functions_in_all_forms()
    {
        AssertExpression("${header.foo}", "abc");
        AssertExpression("${header:foo}", "abc");
        AssertExpression("${header[foo]}", "abc");
        AssertExpression("${header['foo']}", "abc");
        AssertExpression("${header[\"foo\"]}", "abc");
        AssertExpression("${headers.foo}", "abc");
        AssertExpression("${headers[foo]}", "abc");
        AssertExpression("${in.header.foo}", "abc");
        AssertExpression("${in.headers.foo}", "abc");
        AssertExpression("${header.FOO}", "abc");
        AssertExpression("${header.bar}", 123);
        AssertExpression("${header.missing}", null);
        AssertExpression("${header[dotted.name]}", "dotted");
        AssertExpression("${header.dotted.name}", "dotted");
        Assert.Same(Exchange.Message.Headers, Eval("${headers}"));
    }

    [Fact]
    public void HeaderAs_converts_the_header()
    {
        AssertExpression("${headerAs(bar,String)}", "123");
        AssertExpression("${headerAs('bar', long)}", 123L);
        AssertExpression("${headerAs(missing,int)}", null);
    }

    [Fact]
    public void Exchange_property_functions()
    {
        AssertExpression("${exchangeProperty.prop}", "propValue");
        AssertExpression("${exchangeProperty[prop]}", "propValue");
        AssertExpression("${exchangeProperty:prop}", "propValue");
        AssertExpression("${exchangeProperty.missing}", null);
        Exchange.SetProperty("n", "7");
        AssertExpression("${exchangePropertyAs(n,int)}", 7);
    }

    [Fact]
    public void Exchange_and_route_functions()
    {
        Context.Name = "myContext";
        Exchange.SetProperty(ExchangePropertyKeys.StepId, "step1");

        AssertExpression("${exchangeId}", Exchange.ExchangeId);
        AssertExpression("${id}", Exchange.Message.MessageId);
        AssertExpression("${messageId}", Exchange.Message.MessageId);
        AssertExpression("${routeId}", "currentRoute");
        AssertExpression("${fromRouteId}", "fromRoute");
        AssertExpression("${camelId}", "myContext");
        AssertExpression("${contextName}", "myContext");
        AssertExpression("${stepId}", "step1");
        Assert.Same(Exchange, Eval("${exchange}"));
        AssertExpression("${exchange.ExchangeId}", Exchange.ExchangeId);
        AssertExpression("${exchange.exchangeId}", Exchange.ExchangeId);
        AssertExpression("${exchange.Message.Body}", Exchange.Message.Body);
        AssertExpression("${exchange.properties[prop]}", "propValue");
        Exchange.CurrentRouteId = null;
        AssertExpression("${routeId}", "fromRoute");
    }

    [Fact]
    public void Exception_functions_read_the_exception_or_the_caught_one()
    {
        AssertExpression("${exception}", null);
        AssertExpression("${exception.message}", null);
        AssertExpression("${exception.stacktrace}", null);

        var failure = new InvalidOperationException("boom");
        Exchange.Exception = failure;
        Assert.Same(failure, Eval("${exception}"));
        AssertExpression("${exception.message}", "boom");
        AssertExpression("${exception.Message}", "boom");
        AssertExpression("Error: ${exception.message}", "Error: boom");
        Assert.Contains("InvalidOperationException: boom", (string)Eval("${exception.stacktrace}")!);

        Exchange.Exception = null;
        Exchange.SetProperty(ExchangePropertyKeys.ExceptionCaught, new ArgumentException("caught"));
        AssertExpression("${exception.message}", "caught");
        AssertExpression("${exception.GetType().Name}", "ArgumentException");
    }

    [Fact]
    public void Misc_functions()
    {
        AssertExpression("${null}", null);
        Assert.True(Guid.TryParse((string)Eval("${uuid}")!, out _));
        Assert.NotEqual(Eval("${uuid}"), Eval("${uuid}"));
        Assert.Matches("^[0-9a-f]{32}$", (string)Eval("${uuid:simple}")!);
        AssertExpression("${empty(string)}", string.Empty);
        Assert.IsType<List<object?>>(Eval("${empty(list)}"));
        Assert.IsType<Dictionary<string, object?>>(Eval("${empty(map)}"));
        Assert.IsType<HashSet<object?>>(Eval("${empty(set)}"));
        Assert.NotSame(Eval("${empty(list)}"), Eval("${empty(list)}"));
        AssertExpression("${hostname}", Environment.MachineName);
        Assert.IsType<int>(Eval("${threadId}"));
        Assert.IsType<string>(Eval("${threadName}"));
    }

    [Fact]
    public void Random_stays_within_bounds()
    {
        for (var i = 0; i < 200; i++)
        {
            Assert.InRange((int)Eval("${random(1,10)}")!, 1, 9);
            Assert.InRange((int)Eval("${random(5)}")!, 0, 4);
            Assert.InRange((int)Eval("${random( 3 , 4 )}")!, 3, 3);
        }

        Exchange.Message.SetHeader("max", 2);
        Assert.InRange(Convert.ToInt32(Eval("${random(1,${header.max})}")), 1, 1);
    }

    [Fact]
    public void Environment_and_system_functions()
    {
        var name = "CARAVAN_SIMPLE_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "envValue");
        try
        {
            AssertExpression("${env:" + name + "}", "envValue");
            AssertExpression("${env." + name + "}", "envValue");
            AssertExpression("${sysenv." + name + "}", "envValue");
            AssertExpression("${env:" + name + "_MISSING}", null);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        AppContext.SetData("caravan.simple.test", "sysValue");
        AssertExpression("${sys.caravan.simple.test}", "sysValue");
        AssertExpression("${sys.caravan.simple.missing}", null);
    }

    [Fact]
    public void Type_reads_static_fields_constants_and_properties()
    {
        AssertExpression("${type:Caravan.ExchangePropertyKeys.SplitIndex}", ExchangePropertyKeys.SplitIndex);
        AssertExpression("${type:System.Int32.MaxValue}", int.MaxValue);
        AssertExpression("${type:System.String.Empty}", string.Empty);
        AssertExpression("${type:Caravan.Language.SimpleLanguage.DefaultCacheSize}", 1000);
        Assert.Throws<ExpressionEvaluationException>(() => Eval("${type:Caravan.ExchangePropertyKeys.Nope}"));
        Assert.Throws<ExpressionEvaluationException>(() => Eval("${type:No.Such.Type.Field}"));
    }

    [Fact]
    public void Ref_looks_the_bean_up()
    {
        var bean = new Order("o1", 5m);
        Context.Registry.Bind("myBean", bean);

        Assert.Same(bean, Eval("${ref:myBean}"));
        AssertExpression("${ref:nope}", null);
    }

    [Fact]
    public void Collate_skip_and_join_iterate_the_body()
    {
        Exchange.Message.Body = new List<int> { 1, 2, 3, 4, 5 };

        var collated = ((IEnumerable)Eval("${collate(2)}")!).Cast<List<object?>>().ToList();
        Assert.Equal([[1, 2], [3, 4], [5]], collated.Select(g => g.Cast<int>().ToArray()).ToArray());
        Assert.Equal([3, 4, 5], ((IEnumerable)Eval("${skip(2)}")!).Cast<int>());
        AssertExpression("${join()}", "1,2,3,4,5");
        AssertExpression("${join(;)}", "1;2;3;4;5");
        AssertExpression("${join('&','id=')}", "id=1&id=2&id=3&id=4&id=5");

        Exchange.Message.SetHeader("ids", new[] { "a", "b" });
        AssertExpression("${join('|','',header.ids)}", "a|b");
        AssertExpression("${join('|','x',${header.ids})}", "xa|xb");

        Exchange.Message.Body = "a, b,c";
        Assert.Equal(["a", "b", "c"], ((IEnumerable)Eval("${skip(0)}")!).Cast<string>());
        Exchange.Message.Body = null;
        Assert.Empty(((IEnumerable)Eval("${collate(3)}")!).Cast<object>());
    }

    [Fact]
    public void Properties_function_uses_the_properties_component()
    {
        Context.PropertiesComponent.InitialProperties["greeting"] = "Hello";
        Context.PropertiesComponent.InitialProperties["nested"] = "{{greeting}} there";

        AssertExpression("${properties:greeting}", "Hello");
        AssertExpression("${properties:nested} ${body}", "Hello there <hello id='m123'>world!</hello>");
        AssertExpression("${properties:missing:fallback}", "fallback");
        AssertExpression("${properties:missing:}", string.Empty);
        AssertExpression("${propertiesExist:greeting}", true);
        AssertExpression("${propertiesExist:missing}", false);
        AssertExpression("${propertiesExist:!missing}", true);
        Assert.Throws<ExpressionEvaluationException>(() => Eval("${properties:missing}"));
    }

    [Fact]
    public void Placeholders_are_resolved_before_evaluation()
    {
        Context.PropertiesComponent.InitialProperties["name"] = "foo";
        Context.PropertiesComponent.InitialProperties["fn"] = "header.foo";

        AssertExpression("{{name}}-${header.{{name}}}", "foo-abc");
        AssertExpression("${ {{fn}} }", "abc");
        AssertExpression("mock:{{name}}?x={{?nope}}", "mock:foo?x=");
        Assert.Throws<ArgumentException>(() => Eval("{{missing}}"));
    }

    [Fact]
    public void Nested_functions_build_the_function_text()
    {
        Exchange.Message.SetHeader("which", "foo");
        Exchange.Message.SetHeader("abc", "nested!");

        AssertExpression("${header.${header.which}}", "abc");
        AssertExpression("${header.${header.${header.which}}}", "nested!");
        AssertExpression("${exchangeProperty.${header.missing}prop}", "propValue");
    }

    [Fact]
    public void Escapes_produce_special_characters_and_literal_dollars()
    {
        AssertExpression("a\\nb\\tc\\rd", "a\nb\tc\rd");
        AssertExpression("\\${body}", "${body}");
        AssertExpression("Price: $${header.bar}", "Price: $123");
        AssertExpression("C:\\temp", "C:\temp");
        AssertExpression("C:\\data", "C:\\data");
        AssertExpression("${body} \\n", "<hello id='m123'>world!</hello> \n");
    }

    [Fact]
    public void Increment_and_decrement_update_the_source()
    {
        Exchange.Message.SetHeader("counter", 5);
        Exchange.Message.SetHeader("text", "9");
        Exchange.SetProperty("p", 1L);
        Exchange.Message.Body = 10;

        AssertExpression("${header.counter}++", 6);
        Assert.Equal(6, Exchange.Message.GetHeader("counter"));
        AssertExpression("${header.counter}--", 5);
        AssertExpression("${header.text}++", "10");
        Assert.Equal("10", Exchange.Message.GetHeader("text"));
        AssertExpression("${exchangeProperty.p}++", 2L);
        Assert.Equal(2L, Exchange.GetProperty("p"));
        AssertExpression("${body}--", 9);
        Assert.Equal(9, Exchange.Message.Body);
        AssertExpression("${header.missing}++", null);
        AssertExpression("${header.counter}++ items", "6 items");
        AssertExpression("${random(1,2)}++", 2);
        Assert.Throws<ExpressionEvaluationException>(() => Eval("${header.foo}++"));
    }

    [Fact]
    public void Custom_functions_are_an_extension_point()
    {
        Caravan.Language.SimpleLanguage.RegisterFunction("upper", remainder => Expr.Constant(remainder.ToUpperInvariant()));
        try
        {
            AssertExpression("${upper:abc}", "ABC");
        }
        finally
        {
            Assert.True(Caravan.Language.SimpleLanguage.UnregisterFunction("upper"));
        }

        Assert.Throws<SimpleParseException>(() => Eval("${upper:abc}"));
        var bean = Assert.Throws<SimpleParseException>(() => Eval("${bean:foo?method=bar}"));
        Assert.Contains("bean:", bean.Message, StringComparison.Ordinal);
    }
}
