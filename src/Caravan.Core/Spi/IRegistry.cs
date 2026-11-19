namespace Caravan.Spi;

/// <summary>Named and typed lookup of beans (Camel's <c>Registry</c>).</summary>
public interface IRegistry
{
    void Bind(string name, object bean);

    bool Unbind(string name);

    object? Lookup(string name);

    T? Lookup<T>(string name)
        where T : class;

    /// <summary>Finds all beans assignable to <typeparamref name="T"/>, keyed by name.</summary>
    IReadOnlyDictionary<string, T> FindByType<T>()
        where T : class;

    /// <summary>Finds the single bean of the given type, or null if there is none or several.</summary>
    T? FindSingleByType<T>()
        where T : class;
}
