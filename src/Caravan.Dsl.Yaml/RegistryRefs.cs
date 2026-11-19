using Caravan.Builder;
using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Dsl.Yaml;

/// <summary>Objects referring to registry beans by name, looked up when used (so beans may be bound after the routes are loaded).</summary>
internal static class RegistryRefs
{
    /// <summary>A processor delegating to the registry bean <paramref name="name"/> (as <c>process: { ref: name }</c>).</summary>
    public static IProcessor Processor(string name) => ((ProcessDefinition)Dsl.Single(s => s.ProcessRef(name))).Processor;

    /// <summary>An <c>onPrepare</c> callback calling the registry bean <paramref name="name"/> (an <see cref="IProcessor"/> or <see cref="Action{T}"/>).</summary>
    public static Action<Exchange> OnPrepare(string name) => exchange =>
    {
        switch (exchange.Context.Registry.Lookup(name))
        {
            case IProcessor processor:
                processor.ProcessAsync(exchange).AsTask().GetAwaiter().GetResult();
                break;
            case Action<Exchange> action:
                action(exchange);
                break;
            case null:
                throw new NoSuchBeanException(name);
            case var other:
                throw new InvalidOperationException($"Bean {name} ({other.GetType().Name}) is not a processor");
        }
    };

    /// <summary>A predicate calling the registry bean <paramref name="name"/> (an <see cref="IPredicate"/>, as the <c>ref</c> language).</summary>
    public static IPredicate Predicate(string name) => new Language.LanguageExpression("ref", name);

    /// <summary>A comparer delegating to the registry bean <paramref name="name"/> (an <see cref="IComparer{T}"/> of objects).</summary>
    public static IComparer<object?> Comparer(ICaravanContext context, string name) => Comparer<object?>.Create((a, b) =>
        (context.Registry.Lookup(name) as IComparer<object?> ?? throw new NoSuchBeanException(name)).Compare(a, b));
}
