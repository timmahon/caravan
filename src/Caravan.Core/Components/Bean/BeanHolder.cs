using System.Runtime.CompilerServices;
using Caravan.Support;

namespace Caravan.Components.Bean;

/// <summary>
/// Supplies the bean of a <c>bean</c> node, endpoint or expression (Camel's <c>BeanHolder</c>): a given instance, a registry bean looked up by
/// name (falling back to the service registered in the context's service provider for the type of that name), or an instance of a type created with <see cref="BeanActivator"/>, kept according to the <see cref="BeanScope"/> (per context for
/// singletons, per exchange for <see cref="BeanScope.Request"/>). Options (<c>bean.name=value</c>) are set on beans it creates or looks up.
/// </summary>
internal sealed class BeanHolder
{
    private readonly object? _instance;
    private readonly ConditionalWeakTable<ICaravanContext, StrongBox<object?>> _singletons = new();
    private readonly string _requestKey;

    private BeanHolder(object? instance, string? name, Type? type, BeanScope scope, IReadOnlyDictionary<string, string>? options)
    {
        _instance = instance;
        Name = name;
        Type = type ?? instance?.GetType();
        Scope = scope;
        Options = options;
        _requestKey = "CaravanBeanRequestScope:" + (name ?? Type?.FullName);
    }

    /// <summary>The registry name, for a registry bean.</summary>
    public string? Name { get; }

    /// <summary>The bean type when known without a lookup (instance and type beans).</summary>
    public Type? Type { get; }

    public BeanScope Scope { get; }

    public IReadOnlyDictionary<string, string>? Options { get; }

    public static BeanHolder ForInstance(object bean) => new(bean, null, null, BeanScope.Singleton, null);

    public static BeanHolder ForName(string name, BeanScope scope = BeanScope.Singleton, IReadOnlyDictionary<string, string>? options = null) =>
        new(null, name, null, scope, options);

    public static BeanHolder ForType(Type type, BeanScope scope = BeanScope.Singleton, IReadOnlyDictionary<string, string>? options = null) =>
        new(null, null, type, scope, options);

    /// <summary>The bean for <paramref name="exchange"/> (null for a static class).</summary>
    /// <exception cref="NoSuchBeanException">A registry bean is missing.</exception>
    public object? GetBean(Exchange exchange)
    {
        if (_instance is not null)
        {
            return _instance;
        }

        switch (Scope)
        {
            case BeanScope.Prototype:
                return Obtain(exchange.Context);
            case BeanScope.Request:
                if (exchange.Properties.TryGetValue(_requestKey, out var existing))
                {
                    return existing;
                }

                var bean = Obtain(exchange.Context);
                exchange.SetProperty(_requestKey, bean);
                return bean;
            default:
                return GetSingleton(exchange.Context);
        }
    }

    /// <summary>The singleton bean of <paramref name="context"/>, looked up or created on first use.</summary>
    public object? GetSingleton(ICaravanContext context)
    {
        if (_instance is not null)
        {
            return _instance;
        }

        if (_singletons.TryGetValue(context, out var box))
        {
            return box.Value;
        }

        lock (_singletons)
        {
            if (!_singletons.TryGetValue(context, out box))
            {
                box = new StrongBox<object?>(Obtain(context));
                _singletons.Add(context, box);
            }

            return box.Value;
        }
    }

    public override string ToString() => Name ?? Type?.Name ?? "bean";

    /// <summary>The service registered in the context's service provider for the type named <paramref name="name"/>, if any.</summary>
    private static object? FromServices(ICaravanContext context, string name) =>
        (context as CaravanContext)?.Services is { } services && Language.Simple.TypeNameResolver.Resolve(name) is { } type
            ? services.GetService(type)
            : null;

    private object? Obtain(ICaravanContext context)
    {
        object? bean;
        if (Name is not null)
        {
            var name = context.ResolvePropertyPlaceholders(Name);
            bean = context.Registry.Lookup(name) ?? FromServices(context, name) ?? throw new NoSuchBeanException(name);
        }
        else
        {
            bean = BeanActivator.Create(context, Type!);
        }

        if (bean is not null && Options is { Count: > 0 } options)
        {
            var unknown = PropertyBinder.Bind(context, bean, options);
            if (unknown.Count > 0)
            {
                throw new ArgumentException($"Unknown bean options for {bean.GetType().Name}: {string.Join(", ", unknown)}");
            }
        }

        return bean;
    }
}
