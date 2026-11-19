using Caravan.Support;

namespace Caravan.Components.DataFormat;

/// <summary>
/// The <c>dataformat</c> component (Camel's <c>dataformat</c>): <c>dataformat:name:marshal|unmarshal[?options]</c> marshals or unmarshals the
/// body with the data format <c>name</c> (<see cref="ICaravanContext.ResolveDataFormat"/>). The options are set on the data format (a new
/// instance for built-in and factory data formats; a data format added to the context or bound in the registry is shared, so configure
/// those directly). Producer only.
/// </summary>
public sealed class DataFormatComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        var separator = remaining.LastIndexOf(':');
        if (separator <= 0)
        {
            throw new ArgumentException("The dataformat endpoint must be dataformat:name:marshal or dataformat:name:unmarshal");
        }

        var name = remaining[..separator];
        var operation = remaining[(separator + 1)..];
        if (!operation.Equals("marshal", StringComparison.Ordinal) && !operation.Equals("unmarshal", StringComparison.Ordinal))
        {
            throw new ArgumentException($"The dataformat operation must be marshal or unmarshal, was: {operation}");
        }

        var context = RequiredContext;
        var dataFormat = context.ResolveDataFormat(name);
        var options = parameters.Where(p => PropertyBinder.HasProperty(dataFormat.GetType(), p.Key)).ToDictionary(p => p.Key, p => p.Value);
        if (options.Count > 0)
        {
            PropertyBinder.Bind(context, dataFormat, options);
            foreach (var key in options.Keys)
            {
                parameters.Remove(key);
            }
        }

        return new DataFormatEndpoint(uri, this, name, operation == "marshal", dataFormat);
    }
}
