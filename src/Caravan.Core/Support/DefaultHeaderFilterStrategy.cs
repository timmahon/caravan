using Caravan.Spi;

namespace Caravan.Support;

/// <summary>
/// A configurable <see cref="IHeaderFilterStrategy"/> (Camel's <c>DefaultHeaderFilterStrategy</c>): names (case-insensitive) and name prefixes
/// filtered in each direction. By default headers starting with <c>Caravan</c> are filtered both ways, so internal headers never leave and
/// callers cannot inject them.
/// </summary>
public class DefaultHeaderFilterStrategy : IHeaderFilterStrategy
{
    /// <summary>The prefix of Caravan's internal headers.</summary>
    public const string CaravanPrefix = "Caravan";

    /// <summary>Message headers never sent to the transport.</summary>
    public HashSet<string> OutFilter { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Transport headers never copied to the message.</summary>
    public HashSet<string> InFilter { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prefixes of message headers never sent to the transport (default <c>Caravan</c>).</summary>
    public List<string> OutFilterStartsWith { get; } = [CaravanPrefix];

    /// <summary>Prefixes of transport headers never copied to the message (default <c>Caravan</c>).</summary>
    public List<string> InFilterStartsWith { get; } = [CaravanPrefix];

    /// <summary>Whether headers whose value is null are filtered out (default true).</summary>
    public bool FilterOnNullValues { get; set; } = true;

    /// <inheritdoc />
    public virtual bool ApplyFilterToCaravanHeaders(string name, object? value, Exchange exchange) =>
        Filter(name, value, OutFilter, OutFilterStartsWith);

    /// <inheritdoc />
    public virtual bool ApplyFilterToExternalHeaders(string name, object? value, Exchange exchange) =>
        Filter(name, value, InFilter, InFilterStartsWith);

    private bool Filter(string name, object? value, HashSet<string> names, List<string> prefixes) =>
        (FilterOnNullValues && value is null)
        || names.Contains(name)
        || prefixes.Exists(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
