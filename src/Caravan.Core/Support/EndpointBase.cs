using Caravan.Spi;

namespace Caravan.Support;

/// <summary>Base class for endpoints. Public settable properties are bound from URI parameters.</summary>
public abstract class EndpointBase : ServiceSupport, IEndpoint
{
    /// <summary>Creates an endpoint.</summary>
    protected EndpointBase(string endpointUri, IComponent? component, ICaravanContext context)
    {
        EndpointUri = endpointUri;
        Component = component;
        Context = context;
    }

    /// <inheritdoc />
    public string EndpointUri { get; }

    /// <inheritdoc />
    public virtual string EndpointKey => EndpointUri;

    /// <inheritdoc />
    public ICaravanContext Context { get; }

    /// <inheritdoc />
    public IComponent? Component { get; }

    /// <inheritdoc />
    public virtual bool IsSingleton => true;

    /// <inheritdoc />
    public virtual bool IsLenientProperties => false;

    /// <summary>The default exchange pattern for exchanges created by this endpoint.</summary>
    public ExchangePattern ExchangePattern { get; set; } = ExchangePattern.InOnly;

    /// <inheritdoc />
    public abstract IProducer CreateProducer();

    /// <inheritdoc />
    public abstract IConsumer CreateConsumer(IProcessor processor);

    /// <summary>Creates an exchange from this endpoint, with <c>InOnly</c> unless <paramref name="pattern"/> is given.</summary>
    public virtual Exchange CreateExchange(ExchangePattern? pattern = null) =>
        new(Context, pattern ?? ExchangePattern) { FromEndpoint = this };

    /// <inheritdoc />
    public override string ToString() => EndpointUri;
}
