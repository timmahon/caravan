using Caravan.Spi;
using Caravan.TypeConversion;

namespace Caravan.Core.Tests.Beans;

public class Greeter
{
    public string Prefix { get; set; } = "Hello";

    public string Greet(string name) => $"{Prefix} {name}";
}

public class GreeterWithLanguage
{
    public string Greet(string name, string lang) => lang == "fr" ? $"Bonjour {name}" : $"Hello {name}";

    public string Shout(string text, int times, bool exclaim) => string.Concat(Enumerable.Repeat(text.ToUpperInvariant(), times)) + (exclaim ? "!" : string.Empty);

    public string Nullable(string? value) => value ?? "was null";
}

public class HandlerBean
{
    public string Other(string body) => "other " + body;

    [Handler]
    public string Handle(string body) => "handled " + body;
}

public class OverloadBean
{
    public string Describe(string text) => "string " + text;

    public string Describe(int number) => "int " + number;

    public string Describe(Order order) => "order " + order.Id;

    public string Describe(string text, int count) => $"string,int {text} {count}";

    public string Describe(int count, string text) => $"int,string {count} {text}";
}

public class AmbiguousBean
{
    public string A(string text) => "a";

    public string B(string text) => "b";
}

public class NoBodyBean
{
    public string Ping() => "pong";

    public string Twice(int value) => (value * 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public class AttributeBean
{
    public string All(
        [Body] string body,
        [Header("count")] int count,
        [Headers] IDictionary<string, object?> headers,
        [ExchangeProperty("prop")] string? prop,
        [ExchangeProperties] IDictionary<string, object?> properties,
        [Simple("${header.count} items")] string summary) =>
        $"{body}|{count}|{headers.Count}|{prop}|{properties.ContainsKey("prop")}|{summary}";

    public string Error([ExchangeException] InvalidOperationException? error, [ExchangeException] ArgumentException? other) =>
        $"{error?.Message}|{other?.Message ?? "none"}";
}

public class AutoBoundBean
{
    public string Inspect(Exchange exchange, Message message, ICaravanContext context, CancellationToken token, ITypeConverter converter, IRegistry registry, string body)
    {
        message.SetHeader("seen", exchange.ExchangeId);
        return $"{body}|{ReferenceEquals(message, exchange.Message)}|{context.Name}|{token.CanBeCanceled}|{converter is not null}|{registry is not null}";
    }
}

public class AsyncBean
{
    public List<string> Calls { get; } = [];

    public async Task<string> UpperAsync(string text)
    {
        await Task.Yield();
        return text.ToUpperInvariant();
    }

    public async Task RecordAsync(string text)
    {
        await Task.Yield();
        lock (Calls)
        {
            Calls.Add(text);
        }
    }

    public async ValueTask<int> LengthAsync(string text)
    {
        await Task.Yield();
        return text.Length;
    }

    public ValueTask NothingAsync() => ValueTask.CompletedTask;

    public async Task<string> FailAsync(string text)
    {
        await Task.Yield();
        throw new InvalidOperationException("async failure " + text);
    }

    public Task<string?> NullAsync() => Task.FromResult<string?>(null);
}

public class VoidBean
{
    public void Touch(Message message) => message.SetHeader("touched", true);

    public string? ReturnsNull() => null;

    public string Throw(string body) => throw new InvalidOperationException("bean failure");
}

public class InstanceIdBean
{
    public Guid Id { get; } = Guid.NewGuid();

    public string WhoAmI() => Id.ToString("N");
}

public static class StaticBean
{
    public static string Twice(string text) => text + text;
}

public class Dependency
{
    public string Name { get; set; } = "dependency";
}

public class InjectedBean(Dependency dependency, ICaravanContext context)
{
    public string Describe(string body) => $"{body} with {dependency.Name} in {context.Name}";
}

public class ProcessorBean : IProcessor
{
    public ValueTask ProcessAsync(Exchange exchange)
    {
        exchange.Message.Body = "processed " + exchange.Message.Body;
        return ValueTask.CompletedTask;
    }
}

public class PredicateBean
{
    public bool IsBig(int amount) => amount > 100;
}

public sealed class DictionaryServiceProvider(Dictionary<Type, object> services) : IServiceProvider
{
    public object? GetService(Type serviceType) => services.GetValueOrDefault(serviceType);
}
