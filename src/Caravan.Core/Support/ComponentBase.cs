using Caravan.Spi;

namespace Caravan.Support;

/// <summary>Base class for components.</summary>
public abstract class ComponentBase : ServiceSupport, IComponent
{
    /// <inheritdoc />
    public ICaravanContext? Context { get; set; }

    /// <summary>The context, or an exception when the component has not been added to one.</summary>
    protected ICaravanContext RequiredContext =>
        Context ?? throw new InvalidOperationException($"Component {GetType().Name} is not attached to a context");

    /// <summary>Parses <paramref name="uri"/>, creates the endpoint and binds the remaining URI parameters to its properties.</summary>
    public IEndpoint CreateEndpoint(string uri)
    {
        var parsed = EndpointUri.Parse(uri);
        var parameters = new Dictionary<string, string>(parsed.Parameters, StringComparer.OrdinalIgnoreCase);
        EndpointBase endpoint;
        try
        {
            endpoint = CreateEndpoint(uri, parsed.Path, parameters);
        }
        catch (CaravanException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new ResolveEndpointFailedException(uri, e.Message, e);
        }

        if (parameters.Count > 0)
        {
            IReadOnlyList<string> unknown;
            try
            {
                unknown = PropertyBinder.Bind(RequiredContext, endpoint, parameters);
            }
            catch (Exception e) when (e is not CaravanException)
            {
                throw new ResolveEndpointFailedException(uri, e.Message, e);
            }

            if (unknown.Count > 0 && !endpoint.IsLenientProperties)
            {
                throw new ResolveEndpointFailedException(
                    uri,
                    $"There are {unknown.Count} parameters that couldn't be set on the endpoint: {string.Join(", ", unknown)}. " +
                    "Check the URI if the parameters are spelt correctly and that they are properties of the endpoint.");
            }

            if (unknown.Count > 0)
            {
                endpoint.OnLenientParameters(unknown.ToDictionary(k => k, k => parameters[k], StringComparer.OrdinalIgnoreCase));
            }
        }

        AfterConfigure(endpoint);
        return endpoint;
    }

    /// <summary>
    /// Creates the endpoint. Implementations may remove parameters they consume themselves from
    /// <paramref name="parameters"/>; the remaining ones are bound to the endpoint's properties.
    /// </summary>
    protected abstract EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters);

    /// <summary>Hook invoked after the endpoint options have been bound.</summary>
    protected virtual void AfterConfigure(EndpointBase endpoint)
    {
    }
}
