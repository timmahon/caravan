using Caravan.Model;

namespace Caravan.Builder;

/// <summary>
/// Configures a <see cref="RouteConfigurationDefinition"/> (Camel's <c>routeConfiguration()</c> DSL):
/// <c>RouteConfiguration("retry", c =&gt; c.OnException&lt;IOException&gt;(o =&gt; o.MaximumRedeliveries(3)).Intercept(i =&gt; i.To("mock:audit")))</c>.
/// </summary>
public sealed class RouteConfigurationDefinitionBuilder
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    /// <param name="definition">The route configuration.</param>
    public RouteConfigurationDefinitionBuilder(RouteConfigurationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }

    /// <summary>The route configuration.</summary>
    public RouteConfigurationDefinition Definition { get; }

    /// <summary>Adds the configuration only when the Simple predicate <paramref name="precondition"/> is true (Camel's <c>precondition</c>).</summary>
    /// <param name="precondition">The predicate, typically on properties.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder Precondition(string precondition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(precondition);
        Definition.Precondition = precondition;
        return this;
    }

    /// <summary>Sets the description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder Description(string description)
    {
        Definition.Description = description;
        return this;
    }

    /// <summary>Sets the error handler of the routes that set none themselves (and whose builder sets none).</summary>
    /// <param name="errorHandler">The error handler, e.g. <c>new DeadLetterChannelDefinition("mock:dead")</c>.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder ErrorHandler(ErrorHandlerDefinition errorHandler)
    {
        ArgumentNullException.ThrowIfNull(errorHandler);
        Definition.ErrorHandler = errorHandler;
        return this;
    }

    /// <summary>Adds an <c>onException</c> clause for <typeparamref name="TException"/>.</summary>
    /// <param name="configure">Configures the clause.</param>
    /// <typeparam name="TException">The exception type.</typeparam>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder OnException<TException>(Action<OnExceptionBuilder>? configure = null)
        where TException : Exception => OnException([typeof(TException)], configure);

    /// <summary>Adds an <c>onException</c> clause for <paramref name="exceptionType"/>.</summary>
    /// <param name="exceptionType">The exception type.</param>
    /// <param name="configure">Configures the clause.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder OnException(Type exceptionType, Action<OnExceptionBuilder>? configure = null) =>
        OnException([exceptionType], configure);

    /// <summary>Adds an <c>onException</c> clause for any of <paramref name="exceptionTypes"/>.</summary>
    /// <param name="exceptionTypes">The exception types.</param>
    /// <param name="configure">Configures the clause.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder OnException(Type[] exceptionTypes, Action<OnExceptionBuilder>? configure = null)
    {
        var definition = new OnExceptionDefinition(exceptionTypes);
        configure?.Invoke(new OnExceptionBuilder(definition));
        Definition.OnExceptions.Add(definition);
        return this;
    }

    /// <summary>Adds an <c>onCompletion</c> block (used by routes without <c>onCompletion</c> of their own or from their builder).</summary>
    /// <param name="configure">Builds the block.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder OnCompletion(Action<OnCompletionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var definition = new OnCompletionDefinition();
        configure(new OnCompletionBuilder(definition));
        Definition.OnCompletions.Add(definition);
        return this;
    }

    /// <summary>Adds an <c>intercept</c> block run before every node of the routes using the configuration.</summary>
    /// <param name="configure">Builds the block.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder Intercept(Action<InterceptBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var definition = new InterceptDefinition();
        configure(new InterceptBuilder(definition));
        Definition.Intercepts.Add(definition);
        return this;
    }

    /// <summary>Adds an <c>interceptFrom</c> block run when an exchange enters a route using the configuration.</summary>
    /// <param name="configure">Builds the block.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder InterceptFrom(Action<InterceptBuilder> configure) => InterceptFrom(null, configure);

    /// <summary>Adds an <c>interceptFrom</c> block for the routes whose endpoint matches <paramref name="uriPattern"/>.</summary>
    /// <param name="uriPattern">The endpoint pattern, or null for all routes.</param>
    /// <param name="configure">Builds the block.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder InterceptFrom(string? uriPattern, Action<InterceptBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var definition = new InterceptFromDefinition { Uri = uriPattern };
        configure(new InterceptBuilder(definition));
        Definition.InterceptFroms.Add(definition);
        return this;
    }

    /// <summary>Adds an <c>interceptSendToEndpoint</c>, registered in the context when a route using the configuration is created.</summary>
    /// <param name="uriPattern">The endpoint pattern.</param>
    /// <param name="configure">Builds the block.</param>
    /// <param name="skipSendToOriginalEndpoint">Whether the original endpoint is skipped after the block.</param>
    /// <param name="afterUri">An endpoint to send to after the original endpoint, if any.</param>
    /// <returns>This builder.</returns>
    public RouteConfigurationDefinitionBuilder InterceptSendToEndpoint(
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
        Definition.InterceptSendToEndpoints.Add(definition);
        return this;
    }
}
