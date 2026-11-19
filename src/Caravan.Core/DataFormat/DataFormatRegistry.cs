using System.Collections.Concurrent;
using Caravan.Spi;

namespace Caravan.DataFormat;

/// <summary>
/// The data formats of a context (<see cref="ICaravanContext.DataFormats"/>). <see cref="Resolve"/> looks a name up in the data formats added
/// to this registry, then in the context's bean registry (an <see cref="IDataFormat"/> bound under the name), then creates a new instance from a
/// factory of this registry (<see cref="AddFactory"/>) or a global one (<see cref="RegisterGlobal"/>, which holds the built-ins: <c>json</c>
/// (alias <c>jackson</c>), <c>xml</c> (alias <c>jaxb</c>), <c>csv</c>, <c>base64</c>, <c>gzipDeflater</c> (alias <c>gzip</c>),
/// <c>zipDeflater</c>, <c>string</c>).
/// </summary>
public sealed class DataFormatRegistry
{
    private static readonly ConcurrentDictionary<string, Func<IDataFormat>> GlobalFactories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["json"] = () => new JsonDataFormat(),
        ["jackson"] = () => new JsonDataFormat(),
        ["xml"] = () => new XmlDataFormat(),
        ["jaxb"] = () => new XmlDataFormat(),
        ["csv"] = () => new CsvDataFormat(),
        ["base64"] = () => new Base64DataFormat(),
        ["gzipDeflater"] = () => new GzipDeflaterDataFormat(),
        ["gzip"] = () => new GzipDeflaterDataFormat(),
        ["zipDeflater"] = () => new ZipDeflaterDataFormat(),
        ["string"] = () => new StringDataFormat(),
    };

    private readonly ConcurrentDictionary<string, IDataFormat> _dataFormats = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<IDataFormat>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICaravanContext _context;

    /// <summary>Creates the registry of <paramref name="context"/>.</summary>
    /// <param name="context">The context whose bean registry is searched.</param>
    public DataFormatRegistry(ICaravanContext context)
    {
        _context = context;
    }

    /// <summary>Registers a data format factory available to every context (used by data format packages).</summary>
    /// <param name="name">The name.</param>
    /// <param name="factory">Creates a new, unconfigured instance.</param>
    public static void RegisterGlobal(string name, Func<IDataFormat> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        GlobalFactories[name] = factory;
    }

    /// <summary>The names of the data formats and factories of this registry and the global ones.</summary>
    public IReadOnlyCollection<string> Names =>
        _dataFormats.Keys.Concat(_factories.Keys).Concat(GlobalFactories.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Adds (or replaces) a configured data format instance of this context, shared by everything resolving the name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="dataFormat">The data format.</param>
    public void Add(string name, IDataFormat dataFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(dataFormat);
        _dataFormats[name] = dataFormat;
    }

    /// <summary>Adds (or replaces) a factory of this context, creating a new instance for each resolution of the name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="factory">Creates the data format.</param>
    public void AddFactory(string name, Func<IDataFormat> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[name] = factory;
    }

    /// <summary>Resolves the data format <paramref name="name"/> (property placeholders are resolved first).</summary>
    /// <param name="name">The name.</param>
    /// <returns>The shared instance added or bound under the name, or a new instance from a factory.</returns>
    /// <exception cref="NoSuchDataFormatException">There is no such data format.</exception>
    public IDataFormat Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = _context.ResolvePropertyPlaceholders(name);
        if (_dataFormats.TryGetValue(name, out var dataFormat))
        {
            return dataFormat;
        }

        if (_context.Registry.Lookup(name) is IDataFormat bean)
        {
            return bean;
        }

        if (_factories.TryGetValue(name, out var factory) || GlobalFactories.TryGetValue(name, out factory))
        {
            return factory();
        }

        throw new NoSuchDataFormatException(name);
    }
}
