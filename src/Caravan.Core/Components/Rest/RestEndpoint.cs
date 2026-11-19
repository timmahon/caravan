using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.Rest;

/// <summary>
/// A <c>rest</c> endpoint: one REST service. Its consumer comes from an <see cref="IRestConsumerFactory"/>: the component named by
/// <see cref="ComponentName"/> or the context's <see cref="Model.Rest.RestConfiguration.Component"/>, else the only
/// <see cref="IRestConsumerFactory"/> bean of the registry, else the <c>platform-http</c> component. Options the endpoint does not know are
/// passed to the factory. There is no producer (calling REST services is the job of an HTTP component).
/// </summary>
public sealed class RestEndpoint : EndpointBase, ILenientEndpoint
{
    /// <summary>The component tried when nothing else is configured.</summary>
    public const string DefaultComponentName = "platform-http";

    private Dictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the endpoint.</summary>
    /// <param name="uri">The endpoint URI.</param>
    /// <param name="component">The component.</param>
    /// <param name="method">The HTTP method in upper case.</param>
    /// <param name="path">The base path.</param>
    /// <param name="uriTemplate">The URI template, or null.</param>
    internal RestEndpoint(string uri, RestComponent component, string method, string path, string? uriTemplate)
        : base(uri, component, component.Context!)
    {
        Method = method;
        Path = path;
        UriTemplate = uriTemplate;
        ExchangePattern = ExchangePattern.InOut;
    }

    /// <summary>The HTTP method in upper case.</summary>
    public string Method { get; }

    /// <summary>The base path.</summary>
    public string Path { get; }

    /// <summary>The URI template (the verb's path), or null.</summary>
    public string? UriTemplate { get; }

    /// <summary>The media types accepted.</summary>
    public string? Consumes { get; set; }

    /// <summary>The media types produced.</summary>
    public string? Produces { get; set; }

    /// <summary>The component creating the consumer (Camel's <c>consumerComponentName</c>); overrides the configuration's.</summary>
    public string? ComponentName { get; set; }

    /// <inheritdoc />
    public override bool IsLenientProperties => true;

    /// <summary>The options passed to the consumer factory.</summary>
    public IReadOnlyDictionary<string, string> Parameters => _parameters;

    /// <inheritdoc />
    public void SetLenientParameters(IDictionary<string, string> parameters) =>
        _parameters = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override IProducer CreateProducer() =>
        throw new NotSupportedException($"Cannot send to {EndpointUri}: the rest component only consumes; use an HTTP component to call REST services");

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor)
    {
        var configuration = Context.GetRestConfiguration();
        var factory = ResolveFactory(Context, ComponentName ?? configuration.Component);
        return factory.CreateConsumer(Context, processor, Method, Path, UriTemplate, Consumes, Produces, configuration, _parameters);
    }

    /// <summary>Finds the consumer factory: the named component, else the only factory bean, else <c>platform-http</c>.</summary>
    /// <param name="context">The context.</param>
    /// <param name="name">The configured component name, or null.</param>
    /// <returns>The factory.</returns>
    /// <exception cref="InvalidOperationException">There is none.</exception>
    public static IRestConsumerFactory ResolveFactory(ICaravanContext context, string? name)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return context.GetComponent(name) as IRestConsumerFactory
                ?? context.Registry.Lookup(name) as IRestConsumerFactory
                ?? throw new InvalidOperationException($"The REST component '{name}' does not exist or cannot serve REST services (it must implement IRestConsumerFactory)");
        }

        if (context.Registry.FindSingleByType<IRestConsumerFactory>() is { } bean)
        {
            return bean;
        }

        return context.GetComponent(DefaultComponentName) as IRestConsumerFactory
            ?? throw new InvalidOperationException(
                "Cannot find a component to serve REST services: add Caravan.AspNetCore (builder.AddAspNetCore()) or set RestConfiguration.Component " +
                "to a component implementing IRestConsumerFactory");
    }
}
