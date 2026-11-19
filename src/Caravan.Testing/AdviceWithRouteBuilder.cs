using Caravan.Builder;
using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Testing;

/// <summary>
/// Advises one route (Camel's <c>AdviceWithRouteBuilder</c>): replace its input, weave its nodes, add steps, intercept and mock endpoints.
/// Passed to the lambda of <see cref="AdviceWith.RouteAsync"/>; the changes are applied when the lambda returns:
/// <code>
/// await AdviceWith.RouteAsync(context, "orders", a =&gt;
/// {
///     a.ReplaceFromWith("direct:start");
///     a.WeaveById("charge").Replace(r =&gt; r.To("mock:charge"));
///     a.WeaveAddLast().To("mock:done");
///     a.MockEndpointsAndSkip("http*");
/// });
/// </code>
/// </summary>
public sealed class AdviceWithRouteBuilder
{
    private readonly List<Action<RouteDefinition>> _tasks = [];

    internal AdviceWithRouteBuilder(ICaravanContext context, RouteDefinition route)
    {
        Context = context;
        Route = route;
    }

    /// <summary>The context of the advised route.</summary>
    public ICaravanContext Context { get; }

    /// <summary>The definition of the advised route (changed when the advice is applied).</summary>
    public RouteDefinition Route { get; }

    /// <summary>The id of the advised route.</summary>
    public string RouteId => Route.Id!;

    /// <summary>Replaces the route's input endpoint, e.g. a <c>timer</c> or broker endpoint with <c>direct:start</c> to drive the test.</summary>
    /// <param name="uri">The new input endpoint URI.</param>
    public void ReplaceFromWith(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        AddTask(route => route.FromUri = uri);
    }

    /// <summary>Selects the nodes whose id matches <paramref name="pattern"/> (exact, wildcard such as <c>to*</c>, or regular expression).</summary>
    /// <param name="pattern">The id pattern.</param>
    /// <returns>The weave builder.</returns>
    public AdviceWithBuilder WeaveById(string pattern) =>
        new(this, $"id: {pattern}", node => node.Id is { } id && PatternHelper.Matches(id, pattern));

    /// <summary>
    /// Selects the nodes sending to an endpoint matching <paramref name="pattern"/> (<c>to</c>, <c>toD</c>, <c>wireTap</c>, <c>enrich</c>,
    /// <c>pollEnrich</c>; exact, wildcard or regular expression, see <see cref="EndpointHelper.MatchEndpoint"/>).
    /// </summary>
    /// <param name="pattern">The endpoint URI pattern.</param>
    /// <returns>The weave builder.</returns>
    public AdviceWithBuilder WeaveByToUri(string pattern) =>
        new(this, $"to uri: {pattern}", node => EndpointUriOf(node) is { } uri && EndpointHelper.MatchEndpoint(Context, uri, pattern));

    /// <summary>
    /// Selects the nodes whose text (<see cref="ProcessorDefinition.Label"/> such as <c>to[mock:a]</c>, or <see cref="object.ToString"/>)
    /// matches <paramref name="pattern"/> (exact, wildcard or regular expression).
    /// </summary>
    /// <param name="pattern">The text pattern.</param>
    /// <returns>The weave builder.</returns>
    public AdviceWithBuilder WeaveByToString(string pattern) =>
        new(this, $"toString: {pattern}", node => PatternHelper.Matches(node.Label, pattern) || PatternHelper.Matches(node.ToString(), pattern));

    /// <summary>Selects the nodes of type <typeparamref name="TDefinition"/> (subtypes included), e.g. <c>WeaveByType&lt;SplitDefinition&gt;()</c>.</summary>
    /// <typeparam name="TDefinition">The definition type.</typeparam>
    /// <returns>The weave builder.</returns>
    public AdviceWithBuilder WeaveByType<TDefinition>()
        where TDefinition : ProcessorDefinition => WeaveByType(typeof(TDefinition));

    /// <summary>Selects the nodes of type <paramref name="type"/> (subtypes included).</summary>
    /// <param name="type">The definition type.</param>
    /// <returns>The weave builder.</returns>
    public AdviceWithBuilder WeaveByType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new(this, $"type: {type.Name}", type.IsInstanceOfType);
    }

    /// <summary>Returns a builder whose steps are added at the start of the route: <c>a.WeaveAddFirst().To("mock:start")</c>.</summary>
    /// <returns>The steps builder.</returns>
    public RouteSteps WeaveAddFirst()
    {
        var steps = new RouteSteps([], Route);
        AddTask(route => route.Outputs.InsertRange(0, steps.Outputs));
        return steps;
    }

    /// <summary>Adds the steps built by <paramref name="steps"/> at the start of the route.</summary>
    /// <param name="steps">Builds the steps.</param>
    public void WeaveAddFirst(Action<RouteSteps> steps) => steps(WeaveAddFirst());

    /// <summary>Returns a builder whose steps are added at the end of the route: <c>a.WeaveAddLast().To("mock:result")</c>.</summary>
    /// <returns>The steps builder.</returns>
    public RouteSteps WeaveAddLast()
    {
        var steps = new RouteSteps([], Route);
        AddTask(route => route.Outputs.AddRange(steps.Outputs));
        return steps;
    }

    /// <summary>Adds the steps built by <paramref name="steps"/> at the end of the route.</summary>
    /// <param name="steps">Builds the steps.</param>
    public void WeaveAddLast(Action<RouteSteps> steps) => steps(WeaveAddLast());

    /// <summary>
    /// Mocks the endpoints matching any of <paramref name="patterns"/> (all endpoints when none): every send to such an endpoint, in the whole
    /// context, also goes first to <c>mock:&lt;uri&gt;</c> (e.g. <c>mock:direct:foo</c>, see <see cref="EndpointHelper.MockEndpointUri"/>).
    /// <c>mock:</c> endpoints themselves are never mocked.
    /// </summary>
    /// <param name="patterns">Endpoint URI patterns (exact, wildcard or regular expression).</param>
    public void MockEndpoints(params string[] patterns) => AddTask(_ => MockEndpointsInterceptor.Register(Context, patterns, skip: false));

    /// <summary>
    /// As <see cref="MockEndpoints"/>, but the exchange is only sent to the mock: the original endpoint is skipped (e.g. to cut off a remote
    /// system).
    /// </summary>
    /// <param name="patterns">Endpoint URI patterns (exact, wildcard or regular expression).</param>
    public void MockEndpointsAndSkip(params string[] patterns) => AddTask(_ => MockEndpointsInterceptor.Register(Context, patterns, skip: true));

    /// <summary>Adds an <c>intercept</c> block to the advised route (see <see cref="RouteBuilder.Intercept"/>).</summary>
    /// <param name="configure">Builds the block.</param>
    /// <returns>The definition.</returns>
    public InterceptDefinition Intercept(Action<InterceptBuilder> configure)
    {
        var definition = new InterceptDefinition();
        configure(new InterceptBuilder(definition, Route));
        AddTask(route => route.Intercepts.Add(definition));
        return definition;
    }

    /// <summary>Adds an <c>interceptFrom</c> block to the advised route (see <see cref="RouteBuilder.InterceptFrom(string?, Action{InterceptBuilder})"/>).</summary>
    /// <param name="uriPattern">The endpoint pattern, or null for any.</param>
    /// <param name="configure">Builds the block.</param>
    /// <returns>The definition.</returns>
    public InterceptFromDefinition InterceptFrom(string? uriPattern, Action<InterceptBuilder> configure)
    {
        var definition = new InterceptFromDefinition { Uri = uriPattern };
        configure(new InterceptBuilder(definition, Route));
        AddTask(route => route.InterceptFroms.Add(definition));
        return definition;
    }

    /// <summary>
    /// Adds an <c>interceptSendToEndpoint</c> (see <see cref="RouteBuilder.InterceptSendToEndpoint"/>); as in Camel it applies to every send in
    /// the context once the advised route is created.
    /// </summary>
    /// <param name="uriPattern">The endpoint pattern.</param>
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
        configure?.Invoke(new InterceptSendToEndpointBuilder(definition, Route));
        AddTask(route => route.InterceptSendToEndpoints.Add(definition));
        return definition;
    }

    internal void AddTask(Action<RouteDefinition> task) => _tasks.Add(task);

    internal void Apply()
    {
        foreach (var task in _tasks)
        {
            task(Route);
        }
    }

    private static string? EndpointUriOf(ProcessorDefinition node) => node switch
    {
        ToDefinition to => to.Uri,
        ToDynamicDefinition toD => toD.Uri.ToString(),
        WireTapDefinition wireTap => wireTap.Uri.ToString(),
        EnrichDefinition enrich => enrich.Uri.ToString(),
        PollEnrichDefinition pollEnrich => pollEnrich.Uri.ToString(),
        _ => null,
    };
}
