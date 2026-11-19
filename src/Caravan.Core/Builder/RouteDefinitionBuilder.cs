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

    /// <summary>Adds route policies (Camel's <c>routePolicy</c>), e.g. a <see cref="RoutePolicies.ThrottlingInflightRoutePolicy"/>.</summary>
    /// <param name="policies">The policies.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder RoutePolicy(params Spi.IRoutePolicy[] policies)
    {
        Definition.RoutePolicies.AddRange(policies);
        return this;
    }

    /// <summary>Adds route policies bound in the registry (Camel's <c>routePolicyRef</c>): comma-separated bean names.</summary>
    /// <param name="names">The bean names.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder RoutePolicyRef(string names)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(names);
        Definition.RoutePolicyRefs = Definition.RoutePolicyRefs is null ? names : Definition.RoutePolicyRefs + "," + names;
        return this;
    }

    /// <summary>
    /// Adds the route only when the Simple predicate <paramref name="precondition"/> is true when the route is added (Camel's route
    /// <c>precondition</c>), e.g. <c>.Precondition("{{orders.enabled}}")</c>.
    /// </summary>
    /// <param name="precondition">The predicate.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder Precondition(string precondition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(precondition);
        Definition.Precondition = precondition;
        return this;
    }

    /// <summary>Prefixes the ids of the route's nodes (Camel's <c>nodePrefixId</c>).</summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder NodePrefixId(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        Definition.NodePrefixId = prefix;
        return this;
    }

    /// <summary>Declares the route's input data type (Camel's <c>inputType</c>), e.g. <c>.InputType("json:Order", validate: true)</c>.</summary>
    /// <param name="type">The data type (<c>scheme:name</c>).</param>
    /// <param name="validate">Whether the input is validated with the context's validator for the type.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder InputType(string type, bool validate = false) => InputType(DataTypes.DataType.Parse(type), validate);

    /// <summary>Declares the route's input data type (Camel's <c>inputType</c>).</summary>
    /// <param name="type">The data type.</param>
    /// <param name="validate">Whether the input is validated with the context's validator for the type.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder InputType(DataTypes.DataType type, bool validate = false)
    {
        Definition.InputType = new DataTypes.DataTypeContract(type, validate);
        return this;
    }

    /// <summary>Declares the route's input as the .NET type <paramref name="type"/>: the body is converted to it on entry.</summary>
    /// <param name="type">The type.</param>
    /// <param name="validate">Whether the input is validated with the context's validator for the type.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder InputType(Type type, bool validate = false) => InputType(DataTypes.DataType.FromType(type), validate);

    /// <summary>Declares the route's output data type (Camel's <c>outputType</c>), applied when the exchange leaves the route.</summary>
    /// <param name="type">The data type (<c>scheme:name</c>).</param>
    /// <param name="validate">Whether the output is validated with the context's validator for the type.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder OutputType(string type, bool validate = false) => OutputType(DataTypes.DataType.Parse(type), validate);

    /// <summary>Declares the route's output data type (Camel's <c>outputType</c>).</summary>
    /// <param name="type">The data type.</param>
    /// <param name="validate">Whether the output is validated with the context's validator for the type.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder OutputType(DataTypes.DataType type, bool validate = false)
    {
        Definition.OutputType = new DataTypes.DataTypeContract(type, validate);
        return this;
    }

    /// <summary>Declares the route's output as the .NET type <paramref name="type"/>.</summary>
    /// <param name="type">The type.</param>
    /// <param name="validate">Whether the output is validated with the context's validator for the type.</param>
    /// <returns>This builder.</returns>
    public RouteDefinitionBuilder OutputType(Type type, bool validate = false) => OutputType(DataTypes.DataType.FromType(type), validate);

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
