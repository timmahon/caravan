using System.Collections.Concurrent;
using Caravan.Spi;

namespace Caravan.Language;

/// <summary>
/// The languages of a context (<see cref="ICaravanContext.Languages"/>). <see cref="Resolve"/> looks a name up in the languages added
/// to this registry, then in the context's bean registry (an <see cref="ILanguage"/> bound under the name), then in the global
/// languages (<see cref="RegisterGlobal"/>, which holds the built-ins).
/// </summary>
public sealed class LanguageRegistry
{
    private static readonly ConcurrentDictionary<string, ILanguage> GlobalLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["simple"] = new SimpleLanguage(),
        ["constant"] = new ConstantLanguage(),
        ["header"] = new HeaderLanguage(),
        ["exchangeProperty"] = new ExchangePropertyLanguage(),
        ["tokenize"] = new TokenizeLanguage(),
        ["ref"] = new RefLanguage(),
    };

    private readonly ConcurrentDictionary<string, ILanguage> _languages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICaravanContext _context;

    /// <summary>Creates the registry of <paramref name="context"/>.</summary>
    public LanguageRegistry(ICaravanContext context)
    {
        _context = context;
    }

    /// <summary>Registers a language available to every context (used by language packages).</summary>
    public static void RegisterGlobal(string name, ILanguage language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(language);
        GlobalLanguages[name] = language;
    }

    /// <summary>The names of the languages added to this registry and the global ones.</summary>
    public IReadOnlyCollection<string> Names =>
        _languages.Keys.Concat(GlobalLanguages.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Adds (or replaces) a language of this context.</summary>
    public void Add(string name, ILanguage language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(language);
        _languages[name] = language;
    }

    /// <summary>Resolves the language <paramref name="name"/>.</summary>
    /// <exception cref="NoSuchLanguageException">There is no such language.</exception>
    public ILanguage Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_languages.TryGetValue(name, out var language))
        {
            return language;
        }

        if (_context.Registry.Lookup(name) is ILanguage bean)
        {
            return bean;
        }

        return GlobalLanguages.TryGetValue(name, out language) ? language : throw new NoSuchLanguageException(name);
    }
}
