namespace Caravan.Spi;

/// <summary>
/// A store of variables shared beyond one exchange (Camel 4's <c>VariableRepository</c>): the built-in <c>global</c> and <c>route</c>
/// repositories, or custom ones added to <see cref="VariableRepositoryRegistry"/> (or bound in the registry) and addressed with
/// their id as prefix, e.g. <c>myRepo:name</c>. Implementations are thread-safe.
/// </summary>
public interface IVariableRepository
{
    /// <summary>The id of the repository, used as the prefix of variable names (<c>global</c>, <c>route</c>, ...).</summary>
    string Id { get; }

    /// <summary>The names and values of the variables.</summary>
    IReadOnlyDictionary<string, object?> Variables { get; }

    /// <summary>Gets a variable, or null when it does not exist.</summary>
    /// <param name="name">The variable name (without the repository prefix).</param>
    /// <returns>The value.</returns>
    object? GetVariable(string name);

    /// <summary>Sets a variable; a null value removes it (as Camel).</summary>
    /// <param name="name">The variable name (without the repository prefix).</param>
    /// <param name="value">The value.</param>
    void SetVariable(string name, object? value);

    /// <summary>Removes a variable.</summary>
    /// <param name="name">The variable name (without the repository prefix).</param>
    /// <returns>The removed value, or null.</returns>
    object? RemoveVariable(string name);

    /// <summary>Removes every variable.</summary>
    void Clear();
}
