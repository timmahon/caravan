using Caravan.Support;

namespace Caravan.Components.Bean;

/// <summary>
/// The <c>bean</c> component (Camel's <c>bean</c>): <c>bean:name[?method=foo&amp;scope=Singleton|Request|Prototype&amp;bean.property=value]</c>
/// calls a method of the registry bean <c>name</c> (see the <c>bean</c> EIP for the method binding); <c>bean.*</c> options are set on the bean.
/// Producer only.
/// </summary>
public class BeanComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(remaining))
        {
            throw new ArgumentException("The bean endpoint requires a bean name, e.g. bean:myBean?method=foo");
        }

        var (scope, options) = TakeBeanOptions(parameters);
        return new BeanEndpoint(uri, this, BeanHolder.ForName(remaining, scope, options), remaining) { Scope = scope };
    }

    /// <summary>Removes the <c>scope</c> and <c>bean.*</c> options from <paramref name="parameters"/>.</summary>
    /// <param name="parameters">The URI parameters.</param>
    /// <returns>The scope and the bean options.</returns>
    protected (BeanScope Scope, IReadOnlyDictionary<string, string> Options) TakeBeanOptions(IDictionary<string, string> parameters)
    {
        var scope = BeanScope.Singleton;
        if (parameters.Remove("scope", out var scopeText))
        {
            scope = Enum.Parse<BeanScope>(RequiredContext.ResolvePropertyPlaceholders(scopeText), ignoreCase: true);
        }

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in parameters.Keys.Where(k => k.StartsWith("bean.", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            options[key[5..]] = parameters[key];
            parameters.Remove(key);
        }

        return (scope, options);
    }
}
