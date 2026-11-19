namespace Caravan.Builder;

/// <summary>
/// Base class for a builder that only defines route configurations (Camel's <c>RouteConfigurationBuilder</c>), usually kept apart from the
/// routes and added to the context before them:
/// <code>
/// public sealed class ErrorConfigurations : RouteConfigurationBuilder
/// {
///     public override void Configuration()
///     {
///         RouteConfiguration("retry", c =&gt; c.OnException&lt;IOException&gt;(o =&gt; o.MaximumRedeliveries(3).Handled(true)));
///         RouteConfiguration(c =&gt; c.ErrorHandler(DeadLetterChannel("mock:dead")));
///     }
/// }
/// </code>
/// </summary>
public abstract class RouteConfigurationBuilder : RouteBuilder
{
    /// <summary>Defines the route configurations with <see cref="RouteBuilder.RouteConfiguration(string?, Action{RouteConfigurationDefinitionBuilder})"/>.</summary>
    public abstract void Configuration();

    /// <inheritdoc />
    public sealed override void Configure() => Configuration();
}
