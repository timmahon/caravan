using Caravan.Builder;
using Caravan.Components.Mock;
using Caravan.Model;
using Caravan.Spi;
using Caravan.Support;
using Microsoft.Extensions.Time.Testing;

namespace Caravan.Testing;

/// <summary>
/// A base class for route tests, independent of the test framework (Camel's <c>CamelTestSupport</c>). Override
/// <see cref="CreateRouteBuilder"/> (or <see cref="CreateRouteBuilders"/>), call <see cref="SetUpAsync"/> before each test and dispose the
/// instance after it. With xUnit:
/// <code>
/// public sealed class OrderRouteTests : CaravanTestSupport, IAsyncLifetime
/// {
///     protected override IRoutesBuilder CreateRouteBuilder() =&gt; new OrderRoutes();
///
///     Task IAsyncLifetime.InitializeAsync() =&gt; SetUpAsync();
///
///     Task IAsyncLifetime.DisposeAsync() =&gt; DisposeAsync().AsTask();
///
///     [Fact]
///     public async Task Routes_orders()
///     {
///         GetMockEndpoint("mock:result").ExpectedBodiesReceived("ok");
///         await Template.SendBodyAsync("direct:orders", "order");
///         await AssertMockEndpointsSatisfiedAsync();
///     }
/// }
/// </code>
/// With <see cref="UseAdviceWith"/> the context is not started by <see cref="SetUpAsync"/>: advise routes with
/// <see cref="AdviceWithAsync"/>, then call <see cref="StartAsync"/>.
/// </summary>
public abstract class CaravanTestSupport : IAsyncDisposable
{
    private CaravanContext? _context;
    private IProducerTemplate? _template;
    private IConsumerTemplate? _consumerTemplate;
    private bool _disposed;

    /// <summary>The context under test (created by <see cref="SetUpAsync"/>).</summary>
    /// <exception cref="InvalidOperationException"><see cref="SetUpAsync"/> was not called.</exception>
    public CaravanContext Context => _context ?? throw NotSetUp();

    /// <summary>A producer template of the context, to send test messages.</summary>
    /// <exception cref="InvalidOperationException"><see cref="SetUpAsync"/> was not called.</exception>
    public IProducerTemplate Template => _template ?? throw NotSetUp();

    /// <summary>A consumer template of the context, to receive messages from endpoints.</summary>
    /// <exception cref="InvalidOperationException"><see cref="SetUpAsync"/> was not called.</exception>
    public IConsumerTemplate ConsumerTemplate => _consumerTemplate ?? throw NotSetUp();

    /// <summary>The context's fake clock when <see cref="UseFakeTimeProvider"/> is true (advance it to fire timers and delays), else null.</summary>
    public FakeTimeProvider? FakeTimeProvider { get; private set; }

    /// <summary>
    /// Whether routes are advised before the context starts (Camel's <c>isUseAdviceWith</c>): <see cref="SetUpAsync"/> then does not start the
    /// context; call <see cref="StartAsync"/> once advised. Default false.
    /// </summary>
    protected virtual bool UseAdviceWith => false;

    /// <summary>Whether the context uses a <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/> (exposed as <see cref="FakeTimeProvider"/>). Default false.</summary>
    protected virtual bool UseFakeTimeProvider => false;

    /// <summary>
    /// An endpoint pattern to mock (Camel's <c>isMockEndpoints</c>): sends to matching endpoints also go to <c>mock:&lt;uri&gt;</c>
    /// (<c>*</c> for all endpoints). Null (default) mocks nothing.
    /// </summary>
    protected virtual string? IsMockEndpoints => null;

    /// <summary>
    /// An endpoint pattern to mock and skip (Camel's <c>isMockEndpointsAndSkip</c>): sends to matching endpoints only go to
    /// <c>mock:&lt;uri&gt;</c>. Null (default) mocks nothing.
    /// </summary>
    protected virtual string? IsMockEndpointsAndSkip => null;

    /// <summary>The graceful shutdown timeout of the context (default 10 seconds, shorter than the production default).</summary>
    protected virtual TimeSpan ShutdownTimeout => TimeSpan.FromSeconds(10);

    /// <summary>
    /// Creates the context, configures it (fake clock, registry beans, endpoint mocking), adds the routes and, unless
    /// <see cref="UseAdviceWith"/>, starts it. Call it before each test (e.g. from xUnit's <c>IAsyncLifetime.InitializeAsync</c>).
    /// </summary>
    /// <returns>A task completing when the context is ready.</returns>
    public async Task SetUpAsync()
    {
        if (_context is not null)
        {
            return;
        }

        var context = CreateCaravanContext();
        _context = context;
        context.ShutdownStrategy.Timeout = ShutdownTimeout;
        if (UseFakeTimeProvider)
        {
            FakeTimeProvider = CreateFakeTimeProvider();
            context.TimeProvider = FakeTimeProvider;
        }

        BindToRegistry(context.Registry);
        if (IsMockEndpoints is { } mock)
        {
            MockEndpointsInterceptor.Register(context, [mock], skip: false);
        }

        if (IsMockEndpointsAndSkip is { } mockAndSkip)
        {
            MockEndpointsInterceptor.Register(context, [mockAndSkip], skip: true);
        }

        foreach (var builder in CreateRouteBuilders())
        {
            context.AddRoutes(builder);
        }

        _template = context.CreateProducerTemplate();
        _consumerTemplate = context.CreateConsumerTemplate();
        if (!UseAdviceWith)
        {
            await StartAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Starts the context (needed once advised when <see cref="UseAdviceWith"/> is true; does nothing when started).</summary>
    /// <returns>A task completing when the context has started.</returns>
    public Task StartAsync() => Context.StartAsync();

    /// <summary>Advises a route (see <see cref="AdviceWith.RouteAsync"/>).</summary>
    /// <param name="routeId">The route id, or null when there is a single route.</param>
    /// <param name="configure">Describes the advice.</param>
    /// <returns>The advised route definition.</returns>
    public Task<RouteDefinition> AdviceWithAsync(string? routeId, Action<AdviceWithRouteBuilder> configure) =>
        AdviceWith.RouteAsync(Context, routeId, configure);

    /// <summary>Gets a mock endpoint, e.g. <c>GetMockEndpoint("mock:result")</c> (the <c>mock:</c> prefix is optional).</summary>
    /// <param name="uri">The mock endpoint URI.</param>
    /// <param name="create">Whether to create it when it does not exist yet; when false a missing endpoint fails.</param>
    /// <returns>The mock endpoint.</returns>
    /// <exception cref="NoSuchEndpointException">It does not exist and <paramref name="create"/> is false.</exception>
    public MockEndpoint GetMockEndpoint(string uri, bool create = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        var full = uri.StartsWith("mock:", StringComparison.OrdinalIgnoreCase) ? uri : "mock:" + uri;
        if (!create)
        {
            var key = EndpointUri.Normalize(full);
            if (!Context.Endpoints.OfType<MockEndpoint>().Any(e => string.Equals(EndpointUri.Normalize(e.EndpointUri), key, StringComparison.Ordinal)))
            {
                throw new NoSuchEndpointException(full, "No mock endpoint with this URI has been created");
            }
        }

        return Context.GetMockEndpoint(full);
    }

    /// <summary>Asserts that all mock endpoints of the context are satisfied.</summary>
    /// <param name="timeout">Overrides each mock's result wait time.</param>
    /// <returns>A task completing when they are, failing with <see cref="MockAssertionException"/> otherwise.</returns>
    public Task AssertMockEndpointsSatisfiedAsync(TimeSpan? timeout = null) => MockEndpoint.AssertIsSatisfiedAsync(Context, timeout);

    /// <summary>Resets all mock endpoints of the context (received messages and expectations).</summary>
    public void ResetMocks() => MockEndpoint.ResetMocks(Context);

    /// <summary>Stops the templates and the context. Safe to call more than once.</summary>
    /// <returns>A task completing when stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisposeCoreAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Creates the context; override to pass an <see cref="IServiceProvider"/> or configure it further.</summary>
    /// <returns>The context.</returns>
    protected virtual CaravanContext CreateCaravanContext() => new();

    /// <summary>Creates the fake clock used when <see cref="UseFakeTimeProvider"/> is true.</summary>
    /// <returns>The fake clock.</returns>
    protected virtual FakeTimeProvider CreateFakeTimeProvider() => new();

    /// <summary>Binds beans in the context's registry before the routes are added (Camel's <c>bindToRegistry</c>).</summary>
    /// <param name="registry">The registry.</param>
    protected virtual void BindToRegistry(IRegistry registry)
    {
    }

    /// <summary>The routes under test; override this or <see cref="CreateRouteBuilders"/>.</summary>
    /// <returns>The routes builder, or null.</returns>
    protected virtual IRoutesBuilder? CreateRouteBuilder() => null;

    /// <summary>The routes under test (by default the one of <see cref="CreateRouteBuilder"/>).</summary>
    /// <returns>The routes builders.</returns>
    protected virtual IEnumerable<IRoutesBuilder> CreateRouteBuilders() => CreateRouteBuilder() is { } builder ? [builder] : [];

    /// <summary>Stops the templates and the context; override to release more resources (call the base implementation).</summary>
    /// <returns>A task completing when stopped.</returns>
    protected virtual async ValueTask DisposeCoreAsync()
    {
        if (_template is not null)
        {
            await _template.DisposeAsync().ConfigureAwait(false);
        }

        if (_consumerTemplate is not null)
        {
            await _consumerTemplate.DisposeAsync().ConfigureAwait(false);
        }

        if (_context is not null)
        {
            await _context.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static InvalidOperationException NotSetUp() =>
        new("The test support is not set up: call SetUpAsync() before the test (e.g. from IAsyncLifetime.InitializeAsync)");
}
