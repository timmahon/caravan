using Caravan.Components.Rest;
using Caravan.Model;
using Caravan.Model.Rest;

namespace Caravan.Builder;

/// <summary>
/// Base class for defining routes in C# (Camel's <c>RouteBuilder</c>):
/// <code>
/// public sealed class OrderRoutes : RouteBuilder
/// {
///     public override void Configure()
///     {
///         From("direct:orders")
///             .RouteId("orders")
///             .SetHeader("received", Expr.Constant(true))
///             .To("log:orders")
///             .To("mock:result");
///     }
/// }
/// </code>
/// </summary>
public abstract class RouteBuilder : IRoutesBuilder
{
    private readonly List<RouteDefinition> _routes = [];
    private readonly List<OnExceptionDefinition> _onExceptions = [];
    private readonly List<OnCompletionDefinition> _onCompletions = [];
    private readonly List<InterceptDefinition> _intercepts = [];
    private readonly List<InterceptFromDefinition> _interceptFroms = [];
    private readonly List<InterceptSendToEndpointDefinition> _interceptSendToEndpoints = [];
    private readonly List<RouteTemplateDefinition> _templates = [];
    private readonly List<TemplatedRouteDefinition> _templatedRoutes = [];
    private readonly List<RouteConfigurationDefinition> _routeConfigurations = [];
    private readonly List<RestDefinition> _rests = [];
    private ErrorHandlerDefinition? _errorHandler;
    private ICaravanContext? _context;

    /// <summary>The context the routes are being added to (available inside <see cref="Configure"/>).</summary>
    protected ICaravanContext Context =>
        _context ?? throw new InvalidOperationException("Context is only available while Configure() runs");

    /// <summary>Defines routes using <see cref="From"/>.</summary>
    public abstract void Configure();

    /// <summary>Starts a new route consuming from <paramref name="uri"/>.</summary>
    public RouteDefinitionBuilder From(string uri)
    {
        var route = new RouteDefinition(uri);
        _routes.Add(route);
        return new RouteDefinitionBuilder(route);
    }

    /// <summary>The REST definitions defined by the last <see cref="Build"/> (their routes are among the built routes).</summary>
    public IReadOnlyList<RestDefinition> RestDefinitions => _rests;

    /// <summary>
    /// Defines a REST API (Camel's <c>rest</c>): <c>Rest("/api/orders", r =&gt; r.Get("/{id}", v =&gt; v.Produces("application/json").To("direct:getOrder")))</c>.
    /// Each service becomes a route consuming from <c>rest:method:path:uriTemplate</c> (served by the component of the
    /// <see cref="Model.Rest.RestConfiguration"/>, e.g. <c>platform-http</c>) with the binding of the configuration; a service sending to a
    /// <c>direct</c> route of this builder is merged with it unless <see cref="Model.Rest.RestConfiguration.InlineRoutes"/> is off.
    /// </summary>
    /// <param name="path">The base path, or null.</param>
    /// <param name="configure">Configures the API and its services.</param>
    /// <returns>The definition.</returns>
    public RestDefinition Rest(string? path, Action<RestBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var definition = new RestDefinition { Path = path };
        configure(new RestBuilder(definition));
        _rests.Add(definition);
        return definition;
    }

    /// <summary>Defines a REST API without base path (services give full paths).</summary>
    /// <param name="configure">Configures the API and its services.</param>
    /// <returns>The definition.</returns>
    public RestDefinition Rest(Action<RestBuilder> configure) => Rest(null, configure);

    /// <summary>
    /// Changes the context's REST configuration (Camel's <c>restConfiguration()</c>, context-wide): component, context path, binding mode,
    /// CORS, OpenAPI path and properties... Only available inside <see cref="Configure"/>.
    /// </summary>
    /// <param name="configure">Changes the configuration.</param>
    /// <returns>The configuration.</returns>
    public Model.Rest.RestConfiguration RestConfiguration(Action<RestConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var configuration = Context.GetRestConfiguration();
        configure(new RestConfigurationBuilder(configuration));
        return configuration;
    }

    /// <summary>The route templates defined by the last <see cref="Build"/>.</summary>
    public IReadOnlyList<RouteTemplateDefinition> RouteTemplateDefinitions => _templates;

    /// <summary>The templated routes declared by the last <see cref="Build"/>.</summary>
    public IReadOnlyList<TemplatedRouteDefinition> TemplatedRouteDefinitions => _templatedRoutes;

    /// <summary>The route configurations defined by the last <see cref="Build"/>.</summary>
    public IReadOnlyList<RouteConfigurationDefinition> RouteConfigurationDefinitions => _routeConfigurations;

    /// <summary>
    /// Defines a route template (Camel's <c>routeTemplate</c>): <c>RouteTemplate("t").TemplateParameter("name").From("timer:{{name}}").To(...)</c>.
    /// Templates are added to the context with the builder's routes but are not routes: create routes from them with
    /// <see cref="TemplatedRoute"/>, <see cref="TemplatedRouteBuilder"/>, <see cref="ICaravanContext.AddRouteFromTemplateAsync(string?, string, IDictionary{string, object?}, CancellationToken)"/>
    /// or the <c>kamelet</c> component. The builder's error handler, <c>onException</c>, <c>onCompletion</c> and interceptors apply to them.
    /// </summary>
    /// <param name="id">The template id.</param>
    /// <returns>The template builder.</returns>
    public RouteTemplateBuilder RouteTemplate(string id)
    {
        var template = new RouteTemplateDefinition(id);
        _templates.Add(template);
        return new RouteTemplateBuilder(template);
    }

    /// <summary>
    /// Declares a route created from the template <paramref name="templateId"/> when this builder is added to a context (Camel's
    /// <c>templatedRoute</c>): <c>TemplatedRoute("t", r =&gt; r.RouteId("a").Parameter("name", "a"))</c>. The template may come from this
    /// builder or from one added earlier.
    /// </summary>
    /// <param name="templateId">The template id.</param>
    /// <param name="configure">Sets the route id, parameters and beans.</param>
    /// <returns>The definition.</returns>
    public TemplatedRouteDefinition TemplatedRoute(string templateId, Action<TemplatedRouteDefinitionBuilder>? configure = null)
    {
        var definition = new TemplatedRouteDefinition(templateId);
        configure?.Invoke(new TemplatedRouteDefinitionBuilder(definition));
        _templatedRoutes.Add(definition);
        return definition;
    }

    /// <summary>Defines a default route configuration (no id: applied to every route that selects no configuration).</summary>
    /// <param name="configure">Configures it.</param>
    /// <returns>The definition.</returns>
    public RouteConfigurationDefinition RouteConfiguration(Action<RouteConfigurationDefinitionBuilder> configure) => RouteConfiguration(null, configure);

    /// <summary>
    /// Defines a route configuration (Camel's <c>routeConfiguration</c>), added to the context with this builder; routes select it with
    /// <see cref="RouteDefinitionBuilder.RouteConfigurationId"/> (a null <paramref name="id"/> applies it to every route selecting none).
    /// </summary>
    /// <param name="id">The id, or null.</param>
    /// <param name="configure">Configures it.</param>
    /// <returns>The definition.</returns>
    public RouteConfigurationDefinition RouteConfiguration(string? id, Action<RouteConfigurationDefinitionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var definition = new RouteConfigurationDefinition { Id = id };
        configure(new RouteConfigurationDefinitionBuilder(definition));
        _routeConfigurations.Add(definition);
        return definition;
    }

    /// <summary>Sets the error handler of this builder's routes (a route's own error handler takes precedence).</summary>
    public void ErrorHandler(ErrorHandlerDefinition errorHandler)
    {
        ArgumentNullException.ThrowIfNull(errorHandler);
        _errorHandler = errorHandler;
    }

    /// <summary>Creates a default error handler (no redelivery unless configured; failures are logged and propagate).</summary>
    public DefaultErrorHandlerDefinition DefaultErrorHandler() => new();

    /// <summary>Creates a dead letter channel sending exhausted failures to <paramref name="deadLetterUri"/>.</summary>
    public DeadLetterChannelDefinition DeadLetterChannel(string deadLetterUri) => new(deadLetterUri);

    /// <summary>Creates a "no error handler": failures propagate as they are and <c>onException</c> does not apply.</summary>
    public NoErrorHandlerDefinition NoErrorHandler() => new();

    /// <summary>
    /// Adds an <c>onException</c> clause for <typeparamref name="TException"/> applying to all routes of this builder
    /// (route-scoped clauses take precedence): <c>OnException&lt;IOException&gt;(o =&gt; o.Handled(true).To("mock:error"))</c>.
    /// </summary>
    public OnExceptionDefinition OnException<TException>(Action<OnExceptionBuilder>? configure = null)
        where TException : Exception => OnException([typeof(TException)], configure);

    /// <summary>Adds an <c>onException</c> clause for <paramref name="exceptionType"/> applying to all routes of this builder.</summary>
    public OnExceptionDefinition OnException(Type exceptionType, Action<OnExceptionBuilder>? configure = null) =>
        OnException([exceptionType], configure);

    /// <summary>Adds an <c>onException</c> clause for any of <paramref name="exceptionTypes"/> applying to all routes of this builder.</summary>
    public OnExceptionDefinition OnException(Type[] exceptionTypes, Action<OnExceptionBuilder>? configure = null)
    {
        var definition = new OnExceptionDefinition(exceptionTypes);
        configure?.Invoke(new OnExceptionBuilder(definition));
        _onExceptions.Add(definition);
        return definition;
    }

    /// <summary>
    /// Adds an <c>onCompletion</c> block applying to the routes of this builder that have none of their own:
    /// <c>OnCompletion(c =&gt; c.OnFailureOnly().To("mock:failed"))</c>.
    /// </summary>
    public OnCompletionDefinition OnCompletion(Action<OnCompletionBuilder> configure)
    {
        var definition = new OnCompletionDefinition();
        configure(new OnCompletionBuilder(definition));
        _onCompletions.Add(definition);
        return definition;
    }

    /// <summary>
    /// Adds an <c>intercept</c> block run before every node of every route of this builder (Camel's <c>intercept()</c>):
    /// <c>Intercept(i =&gt; i.When(p).To("mock:audit"))</c>. A <c>Stop()</c> in the block stops the exchange.
    /// </summary>
    /// <param name="configure">Builds the block.</param>
    /// <returns>The definition.</returns>
    public InterceptDefinition Intercept(Action<InterceptBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var definition = new InterceptDefinition();
        configure(new InterceptBuilder(definition));
        _intercepts.Add(definition);
        return definition;
    }

    /// <summary>Adds an <c>interceptFrom</c> block run when an exchange enters any route of this builder.</summary>
    /// <param name="configure">Builds the block.</param>
    /// <returns>The definition.</returns>
    public InterceptFromDefinition InterceptFrom(Action<InterceptBuilder> configure) => InterceptFrom(null, configure);

    /// <summary>
    /// Adds an <c>interceptFrom</c> block run when an exchange enters a route of this builder whose endpoint matches
    /// <paramref name="uriPattern"/> (exact, wildcard such as <c>seda:*</c>, or regular expression; null for all routes):
    /// <c>InterceptFrom("seda:*", i =&gt; i.To("mock:received"))</c>. The header and property
    /// <see cref="CaravanHeaders.InterceptedEndpoint"/> hold the route's endpoint URI.
    /// </summary>
    /// <param name="uriPattern">The endpoint pattern, or null.</param>
    /// <param name="configure">Builds the block.</param>
    /// <returns>The definition.</returns>
    public InterceptFromDefinition InterceptFrom(string? uriPattern, Action<InterceptBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var definition = new InterceptFromDefinition { Uri = uriPattern };
        configure(new InterceptBuilder(definition));
        _interceptFroms.Add(definition);
        return definition;
    }

    /// <summary>
    /// Adds an <c>interceptSendToEndpoint</c> block run before exchanges are sent to endpoints matching <paramref name="uriPattern"/>:
    /// <c>InterceptSendToEndpoint("direct:payments", i =&gt; i.To("mock:payments"), skipSendToOriginalEndpoint: true)</c>. Once a route of this
    /// builder is created it applies to every send in the context (routes, EIPs and producer templates), as in Camel.
    /// </summary>
    /// <param name="uriPattern">The endpoint pattern (exact, wildcard or regular expression).</param>
    /// <param name="configure">Builds the block.</param>
    /// <param name="skipSendToOriginalEndpoint">Whether the original endpoint is skipped after the block.</param>
    /// <param name="afterUri">An endpoint to send to after the original endpoint, if any.</param>
    /// <returns>The definition.</returns>
    public InterceptSendToEndpointDefinition InterceptSendToEndpoint(
        string uriPattern,
        Action<InterceptSendToEndpointBuilder>? configure = null,
        bool skipSendToOriginalEndpoint = false,
        string? afterUri = null)
    {
        var definition = new InterceptSendToEndpointDefinition(uriPattern)
        {
            SkipSendToOriginalEndpoint = skipSendToOriginalEndpoint,
            AfterUri = afterUri,
        };
        configure?.Invoke(new InterceptSendToEndpointBuilder(definition));
        _interceptSendToEndpoints.Add(definition);
        return definition;
    }

    /// <inheritdoc />
    public IReadOnlyList<RouteDefinition> Build(ICaravanContext context)
    {
        _context = context;
        try
        {
            _routes.Clear();
            _onExceptions.Clear();
            _onCompletions.Clear();
            _intercepts.Clear();
            _interceptFroms.Clear();
            _interceptSendToEndpoints.Clear();
            _templates.Clear();
            _templatedRoutes.Clear();
            _routeConfigurations.Clear();
            _rests.Clear();
            _errorHandler = null;
            Configure();
            RestRouteFactory.AddRoutes(_rests, _routes, context.GetRestConfiguration().InlineRoutes);
            ApplyBuilderConfiguration();
            foreach (var hook in context.GetExtension<RouteBuilderHooks>()?.Hooks ?? [])
            {
                hook(this, _routes);
            }

            return [.. _routes];
        }
        finally
        {
            _context = null;
        }
    }

    private void ApplyBuilderConfiguration()
    {
        foreach (var route in _routes.Concat(_templates.Select(t => t.Route).OfType<RouteDefinition>()))
        {
            route.ErrorHandler ??= _errorHandler;
            route.OnExceptions.AddRange(_onExceptions.Where(o => !route.OnExceptions.Contains(o)));
            if (route.OnCompletions.Count == 0)
            {
                route.OnCompletions.AddRange(_onCompletions);
            }

            route.Intercepts.AddRange(_intercepts.Where(i => !route.Intercepts.Contains(i)));
            route.InterceptFroms.AddRange(_interceptFroms.Where(i => !route.InterceptFroms.Contains(i)));
            route.InterceptSendToEndpoints.AddRange(_interceptSendToEndpoints.Where(i => !route.InterceptSendToEndpoints.Contains(i)));
        }
    }

    /// <summary>Creates a routes builder from a lambda.</summary>
    public static IRoutesBuilder Create(Action<RouteBuilder> configure) => new LambdaRouteBuilder(configure);

    private sealed class LambdaRouteBuilder(Action<RouteBuilder> configure) : RouteBuilder
    {
        public override void Configure() => configure(this);
    }
}
