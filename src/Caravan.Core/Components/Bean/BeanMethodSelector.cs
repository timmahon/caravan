using System.Collections.Concurrent;
using System.Reflection;

namespace Caravan.Components.Bean;

/// <summary>
/// Chooses the method to call for one method specification of a bean type, following Camel's bean binding:
/// <list type="bullet">
/// <item>With a name, the public methods of that name (exact case first, then ignoring case); with parameters in the specification, those
/// with as many parameters, matching the types (<c>foo(string, *)</c>) or able to take the values (<c>foo(${body}, 5)</c>).</item>
/// <item>Without a name, the public methods of the type (not those of <see cref="object"/>, property accessors, <c>Dispose</c>, ...),
/// restricted to the <see cref="HandlerAttribute"/> ones when there are any.</item>
/// <item>A single candidate is taken. Otherwise, by the body: the methods with one parameter taking the body whose type the body is an
/// instance of (the most specific one), else the only one the body converts to, else the only method without body parameter. Anything else
/// is ambiguous (<see cref="AmbiguousMethodCallException"/>).</item>
/// </list>
/// Choices that depend only on the body's type are cached per type.
/// </summary>
internal sealed class BeanMethodSelector
{
    private readonly BeanInfo _info;
    private readonly BeanMethodSpec? _spec;
    private readonly MethodInfo[] _candidates;
    private readonly BeanMethodInvoker? _single;
    private readonly ConcurrentDictionary<Type, BeanMethodInvoker> _byBodyType = new();

    public BeanMethodSelector(BeanInfo info, BeanMethodSpec? spec, bool staticOnly)
    {
        _info = info;
        _spec = spec;
        IEnumerable<MethodInfo> candidates;
        if (spec is null)
        {
            candidates = info.Operations.Where(m => !staticOnly || m.IsStatic);
            var handlers = candidates.Where(m => m.IsDefined(typeof(HandlerAttribute), inherit: true)).ToArray();
            if (handlers.Length > 0)
            {
                candidates = handlers;
            }
        }
        else
        {
            candidates = info.MethodsNamed(spec.Name).Where(m => !staticOnly || m.IsStatic);
            if (spec.Parameters is { } parameters)
            {
                candidates = candidates.Where(m => m.GetParameters().Length == parameters.Count);
                candidates = spec.IsTypeSelection
                    ? candidates.Where(m => m.GetParameters().Zip(parameters).All(x => x.Second.Type is null || Matches(x.First.ParameterType, x.Second.Type)))
                    : FilterByValues(candidates.ToArray(), parameters);
            }
        }

        _candidates = candidates.ToArray();
        if (_candidates.Length == 0)
        {
            throw new MethodNotFoundException(
                info.Type,
                spec?.Text ?? "(any)",
                spec is null ? "The bean has no public method" : staticOnly ? "No static method matches (there is no bean instance)" : null);
        }

        if (_candidates.Length == 1)
        {
            _single = info.GetInvoker(_candidates[0], spec);
        }
    }

    public IReadOnlyList<MethodInfo> Candidates => _candidates;

    /// <exception cref="AmbiguousMethodCallException">Several methods could be called.</exception>
    /// <exception cref="MethodNotFoundException">No method can take the body.</exception>
    public BeanMethodInvoker Choose(Exchange exchange)
    {
        if (_single is not null)
        {
            return _single;
        }

        var body = exchange.Message.Body;
        var key = body?.GetType() ?? typeof(void);
        if (_byBodyType.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var (method, cacheable) = ChooseByBody(exchange, body);
        var invoker = _info.GetInvoker(method, _spec);
        if (cacheable)
        {
            _byBodyType.TryAdd(key, invoker);
        }

        return invoker;
    }

    private static bool Matches(Type parameterType, Type type) =>
        parameterType == type || Nullable.GetUnderlyingType(parameterType) == type;

    private static IEnumerable<MethodInfo> FilterByValues(MethodInfo[] candidates, IReadOnlyList<BeanParameterToken> parameters)
    {
        if (candidates.Length <= 1)
        {
            return candidates;
        }

        var accepting = candidates.Where(m => m.GetParameters().Zip(parameters).All(x => Accepts(x.First.ParameterType, x.Second))).ToArray();
        return accepting.Length > 0 ? accepting : candidates;
    }

    private static bool Accepts(Type parameterType, BeanParameterToken token)
    {
        if (token.Kind != BeanParameterTokenKind.Literal)
        {
            return true;
        }

        var value = token.Value;
        if (value is null)
        {
            return !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) is not null;
        }

        var target = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (target.IsInstanceOfType(value))
        {
            return true;
        }

        if (value is string && target != typeof(string) && target != typeof(object))
        {
            return false;
        }

        if (value is bool != (target == typeof(bool)))
        {
            return false;
        }

        return target.IsPrimitive || target == typeof(decimal) || target == typeof(string);
    }

    private (MethodInfo Method, bool Cacheable) ChooseByBody(Exchange exchange, object? body)
    {
        var withBody = _candidates
            .Select(m => (Method: m, Parameters: BeanMethodInvoker.BodyParameters(m)))
            .Where(x => x.Parameters.Length == 1)
            .Select(x => (x.Method, Type: x.Parameters[0].ParameterType))
            .ToArray();
        if (body is null)
        {
            var nullable = withBody.Where(x => !x.Type.IsValueType || Nullable.GetUnderlyingType(x.Type) is not null).ToArray();
            if (nullable.Length == 1)
            {
                return (nullable[0].Method, true);
            }
        }
        else
        {
            var exact = withBody.Where(x => x.Type.IsInstanceOfType(body)).ToArray();
            if (exact.Length > 0)
            {
                var specific = exact.Where(x => exact.All(o => o.Type.IsAssignableFrom(x.Type))).ToArray();
                if (specific.Length == 1)
                {
                    return (specific[0].Method, true);
                }

                throw new AmbiguousMethodCallException(exchange, exact.Select(x => x.Method).ToList());
            }

            var convertible = withBody.Where(x => exchange.Context.TypeConverter.TryConvert(x.Type, exchange, body, out _)).ToArray();
            if (convertible.Length == 1)
            {
                return (convertible[0].Method, false);
            }

            if (convertible.Length > 1)
            {
                throw new AmbiguousMethodCallException(exchange, convertible.Select(x => x.Method).ToList());
            }
        }

        var noBody = _candidates.Where(m => BeanMethodInvoker.BodyParameters(m).Length == 0).ToArray();
        if (noBody.Length == 1)
        {
            // Cached only when the choice did not depend on whether the body's value converts.
            return (noBody[0], body is null || withBody.Length == 0);
        }

        throw new AmbiguousMethodCallException(exchange, _candidates);
    }
}
