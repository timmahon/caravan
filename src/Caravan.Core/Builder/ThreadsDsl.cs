using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>threads</c> and <c>poll</c> EIPs.</summary>
public static class ThreadsDsl
{
    /// <summary>
    /// Hands the rest of the block to a bounded worker pool (Camel's chained <c>threads()</c>): <c>.Threads(poolSize: 5).To("direct:slow")</c>.
    /// At most <paramref name="maxPoolSize"/> exchanges run the following steps at once, <paramref name="maxQueueSize"/> wait, the rest are rejected
    /// per <paramref name="rejectedPolicy"/>.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="poolSize">The number of workers kept (default 10).</param>
    /// <param name="maxPoolSize">The maximum number of workers (default 20, at least <paramref name="poolSize"/>).</param>
    /// <param name="maxQueueSize">The queue capacity (default 1000; 0 for none, -1 unbounded).</param>
    /// <param name="rejectedPolicy">What happens when the pool and queue are full.</param>
    /// <param name="threadName">A name for the pool.</param>
    /// <returns>The block.</returns>
    public static T Threads<T>(
        this T steps,
        int poolSize = 10,
        int? maxPoolSize = null,
        int maxQueueSize = 1000,
        ThreadPoolRejectedPolicy rejectedPolicy = ThreadPoolRejectedPolicy.CallerRuns,
        string? threadName = null)
        where T : RouteSteps
    {
        steps.AddStep(Create(poolSize, maxPoolSize, maxQueueSize, rejectedPolicy, threadName));
        return steps;
    }

    /// <summary>Runs <paramref name="body"/> on a bounded worker pool (the block form of <c>threads</c>).</summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="body">The steps run by the workers.</param>
    /// <param name="poolSize">The number of workers kept (default 10).</param>
    /// <param name="maxPoolSize">The maximum number of workers (default 20, at least <paramref name="poolSize"/>).</param>
    /// <param name="maxQueueSize">The queue capacity (default 1000; 0 for none, -1 unbounded).</param>
    /// <param name="rejectedPolicy">What happens when the pool and queue are full.</param>
    /// <param name="threadName">A name for the pool.</param>
    /// <returns>The block.</returns>
    public static T Threads<T>(
        this T steps,
        Action<RouteSteps> body,
        int poolSize = 10,
        int? maxPoolSize = null,
        int maxQueueSize = 1000,
        ThreadPoolRejectedPolicy rejectedPolicy = ThreadPoolRejectedPolicy.CallerRuns,
        string? threadName = null)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(body);
        var definition = steps.AddStep(Create(poolSize, maxPoolSize, maxQueueSize, rejectedPolicy, threadName));
        steps.BuildNested(definition.Outputs, definition, body);
        if (definition.Outputs.Count == 0)
        {
            throw new ArgumentException("The threads block has no steps", nameof(body));
        }

        return steps;
    }

    /// <summary>
    /// Polls a message from an endpoint into the exchange (Camel 4's <c>poll</c>): <c>.Poll("seda:replies", TimeSpan.FromSeconds(5))</c>. The URI
    /// may use Simple functions. Without a message in time the body becomes null.
    /// </summary>
    /// <param name="steps">The block to add to.</param>
    /// <param name="uri">The endpoint URI template.</param>
    /// <param name="timeout">How long to wait (default 20 s; negative waits forever, zero does not wait).</param>
    /// <param name="variableReceive">The variable receiving the message instead of the exchange's message.</param>
    /// <returns>The block.</returns>
    public static T Poll<T>(this T steps, string uri, TimeSpan? timeout = null, string? variableReceive = null)
        where T : RouteSteps
    {
        var definition = new PollDefinition(Language.SimpleLanguage.Expression(uri)) { VariableReceive = variableReceive };
        if (timeout is { } t)
        {
            definition.Timeout = t;
        }

        steps.AddStep(definition);
        return steps;
    }

    private static ThreadsDefinition Create(int poolSize, int? maxPoolSize, int maxQueueSize, ThreadPoolRejectedPolicy rejectedPolicy, string? threadName) =>
        new()
        {
            PoolSize = poolSize,
            MaxPoolSize = maxPoolSize ?? Math.Max(poolSize, 20),
            MaxQueueSize = maxQueueSize,
            RejectedPolicy = rejectedPolicy,
            ThreadName = threadName,
        };
}
