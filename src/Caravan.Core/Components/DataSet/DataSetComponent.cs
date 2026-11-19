using Caravan.Support;

namespace Caravan.Components.DataSet;

/// <summary>
/// The <c>dataset</c> component (Camel's <c>dataset</c>) for load and soak tests: <c>dataset:name[?options]</c> uses the <see cref="IDataSet"/>
/// bound in the registry under <c>name</c>. Consuming from it sends the data set's messages; sending to it checks that they arrive, in order
/// (it is a mock endpoint expecting <see cref="IDataSet.Size"/> messages). See <see cref="DataSetEndpoint"/> for the options.
/// </summary>
public sealed class DataSetComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        var context = RequiredContext;
        var name = context.ResolvePropertyPlaceholders(remaining);
        var dataSet = context.Registry.Lookup(name) switch
        {
            null => throw new NoSuchBeanException(name),
            IDataSet found => found,
            var other => throw new ArgumentException($"Bean '{name}' is a {other.GetType().Name}, not an {nameof(IDataSet)}"),
        };
        return new DataSetEndpoint(uri, this, name, dataSet);
    }
}
