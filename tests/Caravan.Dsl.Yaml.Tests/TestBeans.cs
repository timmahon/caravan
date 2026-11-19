using Caravan.Spi;

namespace Caravan.Dsl.Yaml.Tests;

public sealed class UpperCaseProcessor : IProcessor
{
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.Message.Body = exchange.Message.GetBody<string>()!.ToUpperInvariant();
        return ValueTask.CompletedTask;
    }
}

public sealed class Greeter
{
    public Greeter()
    {
    }

    public Greeter(string greeting, int times)
    {
        Greeting = greeting;
        Times = times;
    }

    public string Greeting { get; set; } = "Hello";

    public int Times { get; set; } = 1;

    public Address Address { get; set; } = new();

    public Address? Other { get; set; }

    public List<string> Tags { get; set; } = [];

    public Greeter? Friend { get; set; }

    public bool Initialized { get; private set; }

    public static Greeter Create(string greeting) => new(greeting + "!", 2);

    public void Init() => Initialized = true;

    public string Greet(string name) => string.Concat(Enumerable.Repeat($"{Greeting} {name}", Times));

    public bool IsVip(string name) => name.StartsWith("vip", StringComparison.Ordinal);

    public static string Shout(string text) => text.ToUpperInvariant() + "!";
}

public sealed class Address
{
    public string? City { get; set; }

    public int Zip { get; set; }
}

public sealed class OrderLine
{
    public string? Item { get; set; }

    public int Quantity { get; set; }
}

public sealed class TestBusinessException(string message) : Exception(message);

public sealed class DescendingComparer : IComparer<object?>
{
    public int Compare(object? x, object? y) => string.CompareOrdinal(y?.ToString(), x?.ToString());
}

public sealed class ReverseLanguage : ILanguage
{
    public IExpression CreateExpression(string expression) =>
        new FuncExpression(e => new string((e.Message.GetBody<string>() ?? string.Empty).Reverse().ToArray()) + expression);

    public IPredicate CreatePredicate(string expression) => new FuncPredicate(_ => expression == "yes");
}
