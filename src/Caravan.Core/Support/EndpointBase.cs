using Caravan.Spi;

namespace Caravan.Support;

/// <summary>Base class for endpoints. Public settable properties are bound from URI parameters.</summary>
public abstract class EndpointBase : ServiceSupport, IEndpoint
{
    protected EndpointBase(string endpointUri, IComponent? component, ICaravanContext context)
    {
        EndpointUri = endpointUri;
        Component = component;
        Context = context;
    }

    public string EndpointUri { get; }

    public virtual string EndpointKey => EndpointUri;

    public ICaravanContext Context { get; }

    public IComponent? Component { get; }

    public virtual bool IsSingleton => true;

    public virtual bool IsLenientProperties => false;

    /// <summary>The default exchange pattern for exchanges created by this endpoint.</summary>
    public ExchangePattern ExchangePattern { get; set; } = ExchangePattern.InOnly;

    public abstract IProducer CreateProducer();

    public abstract IConsumer CreateConsumer(IProcessor processor);

    public virtual Exchange CreateExchange(ExchangePattern? pattern = null) =>
        new(Context, pattern ?? ExchangePattern) { FromEndpoint = this };

    public override string ToString() => EndpointUri;
}

/// <summary>Base class for components.</summary>
public abstract class ComponentBase : ServiceSupport, IComponent
{
    public ICaravanContext? Context { get; set; }

    protected ICaravanContext RequiredContext =>
        Context ?? throw new InvalidOperationException($"Component {GetType().Name} is not attached to a context");

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

/// <summary>Extra hooks for endpoints.</summary>
public static class EndpointBaseExtensions
{
    internal static void OnLenientParameters(this EndpointBase endpoint, IDictionary<string, string> parameters)
    {
        if (endpoint is ILenientEndpoint lenient)
        {
            lenient.SetLenientParameters(parameters);
        }
    }
}

/// <summary>Implemented by lenient endpoints that want the parameters which did not match a property.</summary>
public interface ILenientEndpoint
{
    void SetLenientParameters(IDictionary<string, string> parameters);
}

/// <summary>Base class for producers.</summary>
public abstract class ProducerBase(IEndpoint endpoint) : ServiceSupport, IProducer
{
    public IEndpoint Endpoint { get; } = endpoint;

    public abstract ValueTask ProcessAsync(Exchange exchange);

    public override string ToString() => $"Producer[{Endpoint.EndpointUri}]";
}

/// <summary>Base class for consumers.</summary>
public abstract class ConsumerBase(IEndpoint endpoint, IProcessor processor) : ServiceSupport, IConsumer
{
    public IEndpoint Endpoint { get; } = endpoint;

    public IProcessor Processor { get; } = processor;

    /// <summary>Creates an exchange originating from this consumer's endpoint.</summary>
    protected Exchange CreateExchange(ExchangePattern? pattern = null) => Endpoint.CreateExchange(pattern);

    public override string ToString() => $"Consumer[{Endpoint.EndpointUri}]";
}
