namespace Caravan.Support;

/// <summary>Resolves beans referenced by name in EIP definitions.</summary>
internal static class RegistryLookup
{
    /// <summary>The bean named <paramref name="name"/> (property placeholders resolved) as <typeparamref name="T"/>; null when <paramref name="name"/> is null.</summary>
    /// <exception cref="NoSuchBeanException">No bean is bound under the name.</exception>
    /// <exception cref="InvalidOperationException">The bean is not a <typeparamref name="T"/>.</exception>
    public static T? Lookup<T>(ICaravanContext context, string? name)
        where T : class
    {
        if (name is null)
        {
            return null;
        }

        name = context.ResolvePropertyPlaceholders(name);
        return context.Registry.Lookup(name) switch
        {
            null => throw new NoSuchBeanException(name),
            T found => found,
            var other => throw new InvalidOperationException($"Bean '{name}' is a {other.GetType().Name}, not an {typeof(T).Name}"),
        };
    }
}
