using System.Collections.Concurrent;
using Caravan.Language.Simple;
using Caravan.Spi;

namespace Caravan.Language;

/// <summary>
/// Camel's Simple language. Expressions are text with <c>${...}</c> functions (<c>Hello ${header.name}</c>; a lone function keeps its
/// value's type); predicates combine operands with operators (<c>${header.amount} &gt; 100 &amp;&amp; ${body} contains 'vip'</c>).
/// Texts are compiled once into an immutable tree, kept in an LRU cache (<see cref="CacheSize"/>), and evaluated concurrently.
/// <c>{{placeholders}}</c> are resolved with the exchange's context's <see cref="ICaravanContext.PropertiesComponent"/> before the
/// text is evaluated. See <c>docs/ARCHITECTURE.md</c> ("Simple language") for the functions and operators.
/// </summary>
public sealed class SimpleLanguage : ILanguage
{
    /// <summary>The default number of compiled texts kept per cache (Camel's default too).</summary>
    public const int DefaultCacheSize = 1000;

    private static readonly LruCache<string, SimpleExpression> Expressions = new(DefaultCacheSize);
    private static readonly LruCache<string, SimplePredicate> Predicates = new(DefaultCacheSize);
    private static readonly LruCache<string, SimpleNode> Functions = new(DefaultCacheSize);
    private static readonly ConcurrentDictionary<string, Func<string, IExpression>> Custom = new(StringComparer.Ordinal);

    /// <summary>The maximum number of compiled expressions (and of predicates) cached.</summary>
    public static int CacheSize
    {
        get => Expressions.Capacity;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            Expressions.Capacity = value;
            Predicates.Capacity = value;
            Functions.Capacity = value;
        }
    }

    /// <summary>The number of compiled expressions and predicates currently cached.</summary>
    public static int CachedCount => Expressions.Count + Predicates.Count;

    /// <summary>Custom functions by prefix (see <see cref="RegisterFunction"/>).</summary>
    internal static IEnumerable<KeyValuePair<string, Func<string, IExpression>>> CustomFunctions => Custom;

    /// <summary>Compiles (or gets from the cache) the expression <paramref name="text"/>.</summary>
    /// <exception cref="SimpleParseException">The text is invalid.</exception>
    public static SimpleExpression Expression(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Expressions.GetOrAdd(text, t => new SimpleExpression(t, null, Compile(t, SimpleParser.ParseExpression)));
    }

    /// <summary>Compiles the expression <paramref name="text"/> whose value is converted to <paramref name="resultType"/>.</summary>
    /// <exception cref="SimpleParseException">The text is invalid.</exception>
    public static SimpleExpression Expression(string text, Type? resultType)
    {
        var expression = Expression(text);
        return resultType is null ? expression : new SimpleExpression(text, resultType, expression.Compiled);
    }

    /// <summary>Compiles (or gets from the cache) the predicate <paramref name="text"/>.</summary>
    /// <exception cref="SimpleParseException">The text is invalid.</exception>
    public static SimplePredicate Predicate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Predicates.GetOrAdd(text, t => new SimplePredicate(t, Compile(t, SimpleParser.ParsePredicate)));
    }

    /// <summary>
    /// Registers a custom function: <c>${<paramref name="prefix"/>:remainder}</c> is compiled by calling <paramref name="factory"/>
    /// with the remainder (once per distinct text). This is the extension point of functions provided by other features, such as
    /// <c>bean:</c>. Clears the caches.
    /// </summary>
    public static void RegisterFunction(string prefix, Func<string, IExpression> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(factory);
        Custom[prefix] = factory;
        ClearCache();
    }

    /// <summary>Removes a custom function; returns false when there was none. Clears the caches.</summary>
    public static bool UnregisterFunction(string prefix)
    {
        var removed = Custom.TryRemove(prefix, out _);
        ClearCache();
        return removed;
    }

    /// <summary>Empties the caches of compiled texts.</summary>
    public static void ClearCache()
    {
        Expressions.Clear();
        Predicates.Clear();
        Functions.Clear();
    }

    /// <inheritdoc />
    public IExpression CreateExpression(string expression) => Expression(expression);

    /// <inheritdoc />
    public IPredicate CreatePredicate(string expression) => Predicate(expression);

    /// <summary>Compiles the text of a function evaluated at run time (nested functions); cached.</summary>
    internal static SimpleNode CompileFunction(string function) => Functions.GetOrAdd(function, SimpleParser.ParseFunction);

    private static CompiledSimple Compile(string text, Func<string, SimpleNode> parse)
    {
        var placeholders = text.Contains("{{", StringComparison.Ordinal);
        try
        {
            return new CompiledSimple(parse(text), null, placeholders);
        }
        catch (SimpleParseException e) when (placeholders)
        {
            // The text may only be valid once its placeholders are resolved.
            return new CompiledSimple(null, e, placeholders);
        }
    }
}
