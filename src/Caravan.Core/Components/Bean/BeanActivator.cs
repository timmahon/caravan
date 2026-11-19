using System.Reflection;

namespace Caravan.Components.Bean;

/// <summary>
/// Creates bean instances for <c>.Bean(typeof(X))</c> and <c>class:</c>: the context's <see cref="IServiceProvider"/> is asked first (beans
/// registered in dependency injection), then the public constructor with the most parameters that can all be supplied (the context, services,
/// optional parameters) is called. Static classes have no instance (only their static methods can be called).
/// </summary>
internal static class BeanActivator
{
    /// <summary>Whether instances of <paramref name="type"/> cannot be created because it is a static class.</summary>
    public static bool IsStatic(Type type) => type.IsAbstract && type.IsSealed;

    /// <exception cref="InvalidOperationException">No constructor can be called.</exception>
    public static object? Create(ICaravanContext context, Type type)
    {
        if (IsStatic(type))
        {
            return null;
        }

        var services = (context as CaravanContext)?.Services;
        if (services?.GetService(type) is { } service)
        {
            return service;
        }

        if (type.IsAbstract || type.IsInterface)
        {
            throw new InvalidOperationException($"Cannot create a bean of the abstract type {type.FullName}: register it in the registry or the service provider");
        }

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];
            var resolved = true;
            for (var i = 0; i < parameters.Length && resolved; i++)
            {
                var parameterType = parameters[i].ParameterType;
                if (parameterType.IsInstanceOfType(context))
                {
                    arguments[i] = context;
                }
                else if (services?.GetService(parameterType) is { } dependency)
                {
                    arguments[i] = dependency;
                }
                else if (parameters[i].HasDefaultValue)
                {
                    arguments[i] = parameters[i].DefaultValue;
                }
                else
                {
                    resolved = false;
                }
            }

            if (resolved)
            {
                return constructor.Invoke(arguments);
            }
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        throw new InvalidOperationException(
            $"Cannot create a bean of type {type.FullName}: no public constructor whose parameters can be resolved from the service provider");
    }
}
