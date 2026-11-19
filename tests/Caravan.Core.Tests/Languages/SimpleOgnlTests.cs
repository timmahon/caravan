namespace Caravan.Core.Tests.Languages;

public class SimpleOgnlTests : SimpleTestBase
{
    public SimpleOgnlTests()
    {
        Exchange.Message.Body = new Customer
        {
            Name = "Ann",
            Address = new Address { City = "Oslo", Zip = 1234 },
            Orders = [new Order("o1", 10m), new Order("o2", 20m), new Order("o3", 30m)],
            Tags = new Dictionary<string, string> { ["vip"] = "yes", ["dotted.key"] = "dot" },
            Scores = [7, 8, 9],
        };
    }

    [Fact]
    public void Properties_are_read_along_a_path()
    {
        AssertExpression("${body.Name}", "Ann");
        AssertExpression("${body.Address.City}", "Oslo");
        AssertExpression("${body.Address.Zip}", 1234);
        AssertExpression("${in.body.Address.City}", "Oslo");
        AssertExpression("Lives in ${body.Address.City}", "Lives in Oslo");
    }

    [Fact]
    public void Property_names_match_case_insensitively_like_Java_getters()
    {
        AssertExpression("${body.name}", "Ann");
        AssertExpression("${body.address.city}", "Oslo");
        AssertExpression("${body.getName()}", "Ann");
        AssertExpression("${body.getAddress().getCity()}", "Oslo");
        AssertExpression("${body.isActive()}", true);
        AssertExpression("${body.Field}", "public field");
    }

    [Fact]
    public void Indexers_on_lists_arrays_and_dictionaries()
    {
        AssertExpression("${body.Orders[0].Id}", "o1");
        AssertExpression("${body.Orders[2].Total}", 30m);
        AssertExpression("${body.Orders[last].Id}", "o3");
        AssertExpression("${body.Orders[last-1].Id}", "o2");
        AssertExpression("${body.Scores[1]}", 8);
        AssertExpression("${body.Tags[vip]}", "yes");
        AssertExpression("${body.Tags['vip']}", "yes");
        AssertExpression("${body.Tags[dotted.key]}", "dot");
        AssertExpression("${body.Tags[missing]}", null);
        AssertExpression("${body.Tags.vip}", "yes");
        AssertExpression("${body.Tags.Count}", 2);
        AssertExpression("${body.Orders.size()}", 3);
        AssertExpression("${body.Scores.length}", 3);
        AssertExpression("${body.Lazy[1]}", "b");
    }

    [Fact]
    public void Dictionary_with_non_string_keys_converts_the_key()
    {
        Exchange.Message.Body = new Dictionary<int, string> { [1] = "one" };

        AssertExpression("${body[1]}", "one");
        AssertExpression("${body[2]}", null);
    }

    [Fact]
    public void Out_of_range_index_fails_unless_null_safe()
    {
        Assert.Throws<ExpressionEvaluationException>(() => Eval("${body.Orders[5].Id}"));
        AssertExpression("${body.Orders?[5]?.Id}", null);
        Assert.Throws<ExpressionEvaluationException>(() => Eval("${body.Orders[x]}"));
    }

    [Fact]
    public void Methods_are_invoked_with_constant_arguments()
    {
        AssertExpression("${body.Name.ToUpper()}", "ANN");
        AssertExpression("${body.Name.toupper()}", "ANN");
        AssertExpression("${body.Name.Substring(1)}", "nn");
        AssertExpression("${body.Name.Substring(0, 2)}", "An");
        AssertExpression("${body.Name.Replace('n', 'x')}", "Axx");
        AssertExpression("${body.Name.Contains(\"nn\")}", true);
        AssertExpression("${body.Greet('Hi', 2)}", "Hi Ann Hi Ann");
        AssertExpression("${body.Address.Zip.ToString()}", "1234");
        AssertExpression("${body.Name.Length}", 3);
        AssertExpression("${body.Name.length()}", 3);
    }

    [Fact]
    public void Method_arguments_may_come_from_nested_functions()
    {
        Exchange.Message.SetHeader("start", 2);

        AssertExpression("${body.Name.Substring(${header.start})}", "n");
    }

    [Fact]
    public void Null_root_yields_null_and_null_in_the_path_needs_null_safe_navigation()
    {
        var customer = (Customer)Exchange.Message.Body!;
        customer.Address = null;

        Assert.Throws<ExpressionEvaluationException>(() => Eval("${body.Address.City}"));
        AssertExpression("${body.Address?.City}", null);
        AssertExpression("${body?.Address?.City}", null);
        AssertExpression("City: ${body.Address?.City}", "City: ");

        Exchange.Message.Body = null;
        AssertExpression("${body.Address.City}", null);
    }

    [Fact]
    public void Missing_members_and_failing_methods_raise_evaluation_errors()
    {
        var missing = Assert.Throws<ExpressionEvaluationException>(() => Eval("${body.Nope}"));
        Assert.Contains("Nope", missing.Message, StringComparison.Ordinal);
        Assert.Throws<ExpressionEvaluationException>(() => Eval("${body.Nope()}"));
        Assert.Throws<ExpressionEvaluationException>(() => Eval("${body.Name.Substring('x', 'y')}"));
        var failing = Assert.Throws<ExpressionEvaluationException>(() => Eval("${body.Fail()}"));
        Assert.IsType<InvalidOperationException>(failing.InnerException);
    }

    [Fact]
    public void Header_and_property_values_support_ognl()
    {
        Exchange.Message.SetHeader("customer", Exchange.Message.Body);
        Exchange.SetProperty("customer", Exchange.Message.Body);
        Exchange.Message.SetHeader("customer.Name", "literal header wins");

        AssertExpression("${header.customer.Address.City}", "Oslo");
        AssertExpression("${header[customer].Address.City}", "Oslo");
        AssertExpression("${headers.customer.Orders[1].Id}", "o2");
        AssertExpression("${header.customer.Name}", "literal header wins");
        AssertExpression("${exchangeProperty.customer.Address.City}", "Oslo");
        AssertExpression("${exchangeProperty[customer].Orders[last].Total}", 30m);
        AssertExpression("${header.missing.Address.City}", null);
        AssertExpression("${header.missing?.Address}", null);
    }

    [Fact]
    public void BodyAs_supports_ognl()
    {
        Exchange.Message.Body = 12345;

        AssertExpression("${bodyAs(String).Length}", 5);
        AssertExpression("${mandatoryBodyAs(String).Substring(3)}", "45");
    }

    public sealed class Customer
    {
        public string Field = "public field";

        public string? Name { get; set; }

        public bool Active => true;

        public Address? Address { get; set; }

        public List<Order> Orders { get; set; } = [];

        public Dictionary<string, string> Tags { get; set; } = [];

        public int[] Scores { get; set; } = [];

        public IEnumerable<string> Lazy => new[] { "a", "b" }.Select(x => x);

        public string Greet(string greeting, int times) => string.Join(' ', Enumerable.Repeat($"{greeting} {Name}", times));

        public string Fail() => throw new InvalidOperationException("failed");
    }

    public sealed class Address
    {
        public string? City { get; set; }

        public int Zip { get; set; }
    }
}
