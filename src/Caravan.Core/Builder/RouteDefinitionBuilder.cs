using Caravan.Model;

namespace Caravan.Builder;

/// <summary>Builder for a route: adds route-level options to <see cref="RouteSteps"/>.</summary>
public sealed class RouteDefinitionBuilder : RouteSteps
{
    /// <summary>Creates a builder for <paramref name="definition"/>.</summary>
    public RouteDefinitionBuilder(RouteDefinition definition)
        : base(definition.Outputs, definition)
    {
        Definition = definition;
    }

    /// <summary>The route being built.</summary>
    public RouteDefinition Definition { get; }

    /// <summary>Sets the route id (must be unique in the context).</summary>
    public RouteDefinitionBuilder RouteId(string id)
    {
        Definition.Id = id;
        return this;
    }

    /// <summary>Sets a human-readable description of the route.</summary>
    public RouteDefinitionBuilder RouteDescription(string description)
    {
        Definition.Description = description;
        return this;
    }

    /// <summary>Puts the route in a named group.</summary>
    public RouteDefinitionBuilder RouteGroup(string group)
    {
        Definition.Group = group;
        return this;
    }

    /// <summary>Whether the route starts with the context (default true).</summary>
    public RouteDefinitionBuilder AutoStartup(bool autoStartup)
    {
        Definition.AutoStartup = autoStartup;
        return this;
    }

    /// <summary>Routes with a lower order start first and stop last.</summary>
    public RouteDefinitionBuilder StartupOrder(int order)
    {
        Definition.StartupOrder = order;
        return this;
    }

    /// <summary>
    /// Sets when the route's consumer stops during a graceful shutdown; use <see cref="Caravan.ShutdownRoute.Defer"/>
    /// for routes fed by other routes so they keep consuming until the others are done.
    /// </summary>
    public RouteDefinitionBuilder ShutdownRoute(ShutdownRoute shutdownRoute)
    {
        Definition.ShutdownRoute = shutdownRoute;
        return this;
    }

    /// <summary>
    /// Sets how much pending work the consumer completes during a graceful shutdown;
    /// <see cref="Caravan.ShutdownRunningTask.CompleteAllTasks"/> lets a <c>seda</c> consumer drain its queue.
    /// </summary>
    public RouteDefinitionBuilder ShutdownRunningTask(ShutdownRunningTask shutdownRunningTask)
    {
        Definition.ShutdownRunningTask = shutdownRunningTask;
        return this;
    }

    /// <summary>
    /// Enables or disables stream caching for the route (Camel's <c>streamCache</c> route option), overriding the context's
    /// <see cref="Streams.StreamCachingStrategy.Enabled"/>.
    /// </summary>
    public RouteDefinitionBuilder StreamCaching(bool enabled = true)
    {
        Definition.StreamCaching = enabled;
        return this;
    }

    /// <summary>Sets an arbitrary route property.</summary>
    public RouteDefinitionBuilder RouteProperty(string key, object? value)
    {
        Definition.Properties[key] = value;
        return this;
    }

    /// <summary>
    /// Selects the route configurations the route uses (Camel's <c>routeConfigurationId</c>): a comma-separated list of ids or patterns
    /// (<c>cfg*</c>, regular expressions), applied in that order. Without it the route uses the configurations without id.
    /// </summary>
    /// <param name="ids">The ids or patterns.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder RouteConfigurationId(string ids)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ids);
        Definition.RouteConfigurationIds = ids;
        return this;
    }

    /// <summary>Sets the route's error handler (overrides the route builder's and the context's).</summary>
    public RouteDefinitionBuilder ErrorHandler(ErrorHandlerDefinition errorHandler)
    {
        ArgumentNullException.ThrowIfNull(errorHandler);
        Definition.ErrorHandler = errorHandler;
        return this;
    }

    /// <summary>
    /// Adds a route-scoped <c>onException</c> clause for <typeparamref name="TException"/> (takes precedence over the route builder's):
    /// <c>.OnException&lt;IOException&gt;(o =&gt; o.Handled(true).To("mock:error"))</c>.
    /// </summary>
    public RouteDefinitionBuilder OnException<TException>(Action<OnExceptionBuilder>? configure = null)
        where TException : Exception => OnException([typeof(TException)], configure);

    /// <summary>Adds a route-scoped <c>onException</c> clause for <paramref name="exceptionType"/>.</summary>
    public RouteDefinitionBuilder OnException(Type exceptionType, Action<OnExceptionBuilder>? configure = null) =>
        OnException([exceptionType], configure);

    /// <summary>Adds a route-scoped <c>onException</c> clause for any of <paramref name="exceptionTypes"/>.</summary>
    public RouteDefinitionBuilder OnException(Type[] exceptionTypes, Action<OnExceptionBuilder>? configure = null)
    {
        var definition = new OnExceptionDefinition(exceptionTypes) { RouteScoped = true };
        configure?.Invoke(new OnExceptionBuilder(definition, Definition));
        Definition.OnExceptions.Add(definition);
        return this;
    }

    /// <summary>
    /// Adds a route-scoped <c>onCompletion</c> block (the route then ignores the route builder's):
    /// <c>.OnCompletion(c =&gt; c.OnCompleteOnly().To("mock:done"))</c>.
    /// </summary>
    public RouteDefinitionBuilder OnCompletion(Action<OnCompletionBuilder> configure)
    {
        var definition = new OnCompletionDefinition { RouteScoped = true };
        configure(new OnCompletionBuilder(definition, Definition));
        Definition.OnCompletions.Add(definition);
        return this;
    }
}
