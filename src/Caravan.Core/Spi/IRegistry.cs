namespace Caravan.Spi;

/// <summary>Named and typed lookup of beans (Camel's <c>Registry</c>).</summary>
public interface IRegistry
{
    /// <summary>Binds a bean under <paramref name="name"/>, replacing any existing one.</summary>
    void Bind(string name, object bean);

    /// <summary>Removes a bean; returns whether it existed.</summary>
    bool Unbind(string name);

    /// <summary>Looks up a bean by name, or null.</summary>
    object? Lookup(string name);

    /// <summary>Looks up a bean by name as <typeparamref name="T"/>, or null when it is missing or of another type.</summary>
    T? Lookup<T>(string name)
        where T : class;

    /// <summary>Finds all beans assignable to <typeparamref name="T"/>, keyed by name.</summary>
    IReadOnlyDictionary<string, T> FindByType<T>()
        where T : class;

    /// <summary>Finds the single bean of the given type, or null if there is none or several.</summary>
    T? FindSingleByType<T>()
        where T : class;
}
