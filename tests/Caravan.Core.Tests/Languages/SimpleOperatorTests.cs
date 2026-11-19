using Caravan.Language.Simple;

namespace Caravan.Core.Tests.Languages;

public class SimpleOperatorTests : SimpleTestBase
{
    [Theory]
    [InlineData("${header.foo} == 'abc'", true)]
    [InlineData("${header.foo} == \"abc\"", true)]
    [InlineData("${header.foo} == abc", true)]
    [InlineData("${header.foo} == 'def'", false)]
    [InlineData("${header.foo} == 'ABC'", false)]
    [InlineData("${header.foo} =~ 'ABC'", true)]
    [InlineData("${header.foo} =~ 'ABD'", false)]
    [InlineData("${header.foo} != 'def'", true)]
    [InlineData("${header.foo} != 'abc'", false)]
    [InlineData("${header.foo} !=~ 'ABC'", false)]
    [InlineData("${header.foo} !=~ 'xyz'", true)]
    [InlineData("${header.bar} == 123", true)]
    [InlineData("${header.bar} == '123'", true)]
    [InlineData("${header.bar} == 124", false)]
    [InlineData("${header.bar} != 124", true)]
    [InlineData("${header.missing} == null", true)]
    [InlineData("${header.foo} == null", false)]
    [InlineData("${header.foo} != null", true)]
    [InlineData("${header.missing} != null", false)]
    [InlineData("${header.bar} == ${header.bar}", true)]
    [InlineData("'abc' == ${header.foo}", true)]
    public void Equality_operators_coerce_types(string predicate, bool expected) => AssertPredicate(predicate, expected);

    [Theory]
    [InlineData("${header.bar} > 100", true)]
    [InlineData("${header.bar} > 123", false)]
    [InlineData("${header.bar} >= 123", true)]
    [InlineData("${header.bar} >= 124", false)]
    [InlineData("${header.bar} < 200", true)]
    [InlineData("${header.bar} < 123", false)]
    [InlineData("${header.bar} <= 123", true)]
    [InlineData("${header.bar} <= 122", false)]
    [InlineData("${header.bar} > '100'", true)]
    [InlineData("${header.bar} < 123.5", true)]
    [InlineData("${header.num} > 9", true)]
    [InlineData("${header.num} < 9", false)]
    [InlineData("${header.big} > 2147483647", true)]
    [InlineData("${header.foo} > 'abb'", true)]
    [InlineData("${header.foo} < 'abd'", true)]
    [InlineData("${header.missing} > 1", false)]
    [InlineData("${header.missing} < 1", false)]
    [InlineData("${header.missing} >= ${header.missing}", true)]
    [InlineData("${header.missing} <= null", true)]
    [InlineData("${header.missing} > null", false)]
    [InlineData("${header.bar} >= null", false)]
    public void Comparison_operators_coerce_types_and_handle_null(string predicate, bool expected)
    {
        Exchange.Message.SetHeader("num", "10");
        Exchange.Message.SetHeader("big", 3_000_000_000L);
        AssertPredicate(predicate, expected);
    }

    [Theory]
    [InlineData("${header.foo} contains 'b'", true)]
    [InlineData("${header.foo} contains 'B'", false)]
    [InlineData("${header.foo} !contains 'B'", true)]
    [InlineData("${header.foo} !contains 'b'", false)]
    [InlineData("${header.foo} ~~ 'B'", true)]
    [InlineData("${header.foo} ~~ 'x'", false)]
    [InlineData("${header.foo} !~~ 'B'", false)]
    [InlineData("${header.foo} !~~ 'x'", true)]
    [InlineData("${header.bar} contains 2", true)]
    [InlineData("${header.list} contains 'b'", true)]
    [InlineData("${header.list} contains 'z'", false)]
    [InlineData("${header.list} ~~ 'B'", true)]
    [InlineData("${header.numbers} contains '2'", true)]
    [InlineData("${header.missing} contains 'a'", false)]
    [InlineData("${header.missing} !contains 'a'", true)]
    [InlineData("${body} contains 'world'", true)]
    public void Contains_operators(string predicate, bool expected)
    {
        Exchange.Message.SetHeader("list", new List<string> { "a", "b" });
        Exchange.Message.SetHeader("numbers", new[] { 1, 2, 3 });
        AssertPredicate(predicate, expected);
    }

    [Theory]
    [InlineData("${header.foo} regex '^[a-c]+$'", true)]
    [InlineData("${header.foo} regex '[a-c]+'", true)]
    [InlineData("${header.foo} regex 'b'", false)]
    [InlineData("${header.foo} !regex 'b'", true)]
    [InlineData("${header.foo} regex '\\w{3}'", true)]
    [InlineData("${header.bar} regex '\\d+'", true)]
    [InlineData("${header.missing} regex '.*'", false)]
    [InlineData("${header.missing} !regex '.*'", true)]
    [InlineData("${header.foo} regex ${header.pattern}", true)]
    public void Regex_operators_match_the_whole_value(string predicate, bool expected)
    {
        Exchange.Message.SetHeader("pattern", "a.c");
        AssertPredicate(predicate, expected);
    }

    [Theory]
    [InlineData("${header.foo} in 'abc,def'", true)]
    [InlineData("${header.foo} in 'def, abc'", true)]
    [InlineData("${header.foo} in 'def,ghi'", false)]
    [InlineData("${header.foo} !in 'def,ghi'", true)]
    [InlineData("${header.foo} !in 'abc,ghi'", false)]
    [InlineData("${header.bar} in '1,123,5'", true)]
    [InlineData("${header.bar} in ${header.allowed}", true)]
    [InlineData("${header.foo} in ${header.allowed}", false)]
    [InlineData("${header.missing} in 'a,b'", false)]
    public void In_operators(string predicate, bool expected)
    {
        Exchange.Message.SetHeader("allowed", new[] { 100, 123 });
        AssertPredicate(predicate, expected);
    }

    [Theory]
    [InlineData("${header.bar} range '100..200'", true)]
    [InlineData("${header.bar} range 100..200", true)]
    [InlineData("${header.bar} range '123..123'", true)]
    [InlineData("${header.bar} range '1..122'", false)]
    [InlineData("${header.bar} !range '1..122'", true)]
    [InlineData("${header.bar} !range '100..200'", false)]
    [InlineData("${header.text} range '1..200'", true)]
    [InlineData("${header.missing} range '1..2'", false)]
    [InlineData("${header.bar} range ${header.bounds}", true)]
    public void Range_operators(string predicate, bool expected)
    {
        Exchange.Message.SetHeader("text", "150");
        Exchange.Message.SetHeader("bounds", "120..130");
        AssertPredicate(predicate, expected);
    }

    [Theory]
    [InlineData("${header.foo} is 'String'", true)]
    [InlineData("${header.foo} is String", true)]
    [InlineData("${header.foo} is 'java.lang.String'", true)]
    [InlineData("${header.foo} is 'System.String'", true)]
    [InlineData("${header.foo} is string", true)]
    [InlineData("${header.foo} is 'Integer'", false)]
    [InlineData("${header.foo} !is 'Integer'", true)]
    [InlineData("${header.bar} is 'Integer'", true)]
    [InlineData("${header.bar} is int", true)]
    [InlineData("${header.bar} is 'System.IConvertible'", true)]
    [InlineData("${header.bar} !is 'int'", false)]
    [InlineData("${header.missing} is 'String'", false)]
    [InlineData("${body} is 'Caravan.Core.Tests.Order'", true)]
    [InlineData("${body} is 'Order'", true)]
    [InlineData("${header.list} is 'List'", true)]
    [InlineData("${header.list} is 'Map'", false)]
    public void Is_operators_check_the_type(string predicate, bool expected)
    {
        Exchange.Message.Body = new Order("o1", 10m);
        Exchange.Message.SetHeader("list", new List<int>());
        AssertPredicate(predicate, expected);
    }

    [Theory]
    [InlineData("${header.foo} startsWith 'ab'", true)]
    [InlineData("${header.foo} startsWith 'b'", false)]
    [InlineData("${header.foo} !startsWith 'b'", true)]
    [InlineData("${header.foo} !startsWith 'a'", false)]
    [InlineData("${header.foo} endsWith 'bc'", true)]
    [InlineData("${header.foo} endsWith 'ab'", false)]
    [InlineData("${header.foo} !endsWith 'ab'", true)]
    [InlineData("${header.bar} startsWith 12", true)]
    [InlineData("${header.missing} startsWith 'a'", false)]
    [InlineData("${header.missing} !startsWith 'a'", true)]
    public void StartsWith_and_endsWith_operators(string predicate, bool expected) => AssertPredicate(predicate, expected);

    [Theory]
    [InlineData("${header.foo} == 'abc' && ${header.bar} == 123", true)]
    [InlineData("${header.foo} == 'abc' && ${header.bar} == 124", false)]
    [InlineData("${header.foo} == 'def' && ${header.bar} == 123", false)]
    [InlineData("${header.foo} == 'def' || ${header.bar} == 123", true)]
    [InlineData("${header.foo} == 'def' || ${header.bar} == 124", false)]
    [InlineData("${header.foo} == 'abc' || ${header.bar} == 124", true)]
    [InlineData("${header.foo} == 'abc' && ${header.bar} > 100 && ${body} contains 'world'", true)]
    [InlineData("${header.foo} == 'abc' && ${header.bar} > 100 && ${body} contains 'nope'", false)]
    [InlineData("${header.foo} == 'x' || ${header.bar} == 0 || ${header.bar} == 123", true)]
    [InlineData("${header.flag} && ${header.foo} == 'abc'", true)]
    [InlineData("${header.missing} || ${header.flag}", true)]
    // Left to right with equal precedence, as Camel: (true || false) && false.
    [InlineData("${header.foo} == 'abc' || ${header.bar} == 0 && ${header.bar} == 0", false)]
    // (false && x) || true.
    [InlineData("${header.foo} == 'x' && ${header.bar} == 0 || ${header.bar} == 123", true)]
    public void Logical_operators_evaluate_left_to_right(string predicate, bool expected)
    {
        Exchange.Message.SetHeader("flag", true);
        AssertPredicate(predicate, expected);
    }

    [Fact]
    public void Logical_operators_short_circuit()
    {
        // The right side would fail: the body is not a number.
        Assert.Throws<InvalidPayloadException>(() => Matches("${mandatoryBodyAs(int)} == 1"));
        AssertPredicate("${header.foo} == 'x' && ${mandatoryBodyAs(int)} == 1", false);
        AssertPredicate("${header.foo} == 'abc' || ${mandatoryBodyAs(int)} == 1", true);
    }

    [Theory]
    [InlineData("${header.flag}", true)]
    [InlineData("${header.off}", false)]
    [InlineData("${header.textTrue}", true)]
    [InlineData("${header.textFalse}", false)]
    [InlineData("${header.missing}", false)]
    [InlineData("${header.foo}", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("${header.bar > 100}", true)]
    [InlineData("${header.bar < 100}", false)]
    [InlineData("${header.foo == 'abc' && header.bar == 123}", true)]
    public void Single_operands_follow_Camel_truthiness(string predicate, bool expected)
    {
        Exchange.Message.SetHeader("flag", true);
        Exchange.Message.SetHeader("off", false);
        Exchange.Message.SetHeader("textTrue", "true");
        Exchange.Message.SetHeader("textFalse", "false");
        AssertPredicate(predicate, expected);
    }

    [Fact]
    public void Quoted_literals_may_contain_functions_spaces_and_escapes()
    {
        Exchange.Message.SetHeader("greeting", "hello world");
        Exchange.Message.SetHeader("quote", "it's");
        Exchange.Message.SetHeader("empty", string.Empty);

        AssertPredicate("${header.greeting} == 'hello world'", true);
        AssertPredicate("${header.greeting} == 'hello ${body.Length}'", false);
        AssertPredicate("${header.greeting} == '${header.greeting}'", true);
        AssertPredicate("${header.greeting} == 'hello ${header.missing}world'", true);
        AssertPredicate("${header.quote} == 'it\\'s'", true);
        AssertPredicate("${header.quote} == \"it's\"", true);
        AssertPredicate("${header.greeting} contains ' '", true);
        AssertPredicate("${header.greeting} == ''", false);
        AssertPredicate("${header.empty} == ''", true);
        AssertPredicate("${header.missing} == ''", false);
    }

    [Fact]
    public void Numbers_booleans_and_null_literals()
    {
        Exchange.Message.SetHeader("d", 1.5);
        Exchange.Message.SetHeader("m", 2.25m);
        Exchange.Message.SetHeader("neg", -5);
        Exchange.Message.SetHeader("b", "true");

        AssertPredicate("${header.d} == 1.5", true);
        AssertPredicate("${header.m} == 2.25", true);
        AssertPredicate("${header.m} > 2.2", true);
        AssertPredicate("${header.neg} == -5", true);
        AssertPredicate("${header.neg} < 0", true);
        AssertPredicate("${header.b} == true", true);
        AssertPredicate("${header.b} == false", false);
        AssertPredicate("${header.missing} == null", true);
    }

    [Fact]
    public void Comparing_incomparable_values_fails()
    {
        Exchange.Message.Body = new object();
        var e = Assert.Throws<ExpressionEvaluationException>(() => Matches("${body} > ${exchange}"));
        Assert.Contains(">", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ternary_and_elvis_inside_a_function()
    {
        Exchange.Message.SetHeader("pos", 5);
        Exchange.Message.SetHeader("neg", -5);
        Exchange.Message.SetHeader("empty", string.Empty);
        Exchange.Message.SetHeader("off", false);

        AssertExpression("${header.pos > 0 ? 'positive' : 'negative'}", "positive");
        AssertExpression("${header.neg > 0 ? 'positive' : 'negative'}", "negative");
        AssertExpression("${header.neg > 0 ? 'positive' : header.neg == 0 ? 'zero' : 'negative'}", "negative");
        AssertExpression("${header.pos > 0 ? header.foo : header.bar}", "abc");
        AssertExpression("${header.pos == 5 && header.foo == 'abc' ? ${header.bar} : 0}", 123);
        AssertExpression("Sign: ${header.pos >= 0 ? '+' : '-'}", "Sign: +");
        AssertExpression("${header.flag ? 1 : 2}", 2);
        AssertExpression("${header.foo ?: 'default'}", "abc");
        AssertExpression("${header.missing ?: 'default'}", "default");
        AssertExpression("${header.empty ?: 'default'}", "default");
        AssertExpression("${header.off ?: 'default'}", "default");
        AssertExpression("${header.missing ?: header.bar}", 123);
        AssertExpression("${header.missing ?: header.none ?: 'last'}", "last");
        AssertExpression("${header.missing ?: null}", null);
        AssertPredicate("${header.missing ?: header.foo} == 'abc'", true);
        AssertPredicate("${header.pos > 0 ? true : false}", true);
    }

    [Theory]
    [InlineData("${header.foo} ==", 14)]
    [InlineData("== 'abc'", 0)]
    [InlineData("${header.foo} == 'abc' &&", 23)]
    [InlineData("${header.foo} == 'abc' && || true", 23)]
    [InlineData("${header.foo} 'abc'", 14)]
    [InlineData("${header.foo} == 'abc", 17)]
    [InlineData("${header.foo == 'abc'", 0)]
    [InlineData("${header.foo} range '1-2'", 20)]
    [InlineData("${header.foo} regex '['", 20)]
    [InlineData("${nope} == 1", 2)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void Malformed_predicates_report_the_position(string predicate, int index)
    {
        var e = Assert.Throws<SimpleParseException>(() => Pred.Simple(predicate));
        Assert.Equal(index, e.Index);
        Assert.Equal(predicate, e.Expression);
        Assert.Contains($"at location {index}", e.Message, StringComparison.Ordinal);
    }
}
