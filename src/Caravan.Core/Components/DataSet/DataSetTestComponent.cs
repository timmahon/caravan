using Caravan.Support;

namespace Caravan.Components.DataSet;

/// <summary>
/// The <c>dataset-test</c> component (Camel's <c>dataset-test</c>): <c>dataset-test:expectedUri[?timeout=..&amp;anyOrder=..&amp;split=..]</c> is a
/// mock endpoint that, when it starts, reads the expected messages from <c>expectedUri</c> (polling until nothing arrives within the timeout)
/// and expects their bodies, in order unless <c>anyOrder</c>. See <see cref="DataSetTestEndpoint"/>.
/// </summary>
public sealed class DataSetTestComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(remaining))
        {
            throw new ArgumentException("The dataset-test endpoint requires the URI of the expected messages, e.g. dataset-test:seda:expected");
        }

        return new DataSetTestEndpoint(uri, this, remaining);
    }
}
