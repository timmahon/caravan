using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Builder;

/// <summary>Core DSL: <c>to</c>, <c>process</c>, <c>setBody</c>, <c>transform</c>, <c>setHeader</c>, <c>id</c>.</summary>
public static class CoreDsl
{
    /// <summary>Sends the exchange to an endpoint.</summary>
    public static T To<T>(this T steps, string uri)
        where T : RouteSteps
    {
        steps.AddStep(new ToDefinition(uri));
        return steps;
    }

    /// <summary>Sends the exchange to several endpoints in sequence (pipeline).</summary>
    public static T To<T>(this T steps, params string[] uris)
        where T : RouteSteps
    {
        foreach (var uri in uris)
        {
            steps.AddStep(new ToDefinition(uri));
        }

        return steps;
    }

    /// <summary>Sends the exchange with a specific exchange pattern.</summary>
    public static T To<T>(this T steps, ExchangePattern pattern, string uri)
        where T : RouteSteps
    {
        steps.AddStep(new ToDefinition(uri, pattern));
        return steps;
    }

    /// <summary>Sends as an event message (fire and forget).</summary>
    public static T InOnly<T>(this T steps, string uri)
        where T : RouteSteps => steps.To(ExchangePattern.InOnly, uri);

    /// <summary>Sends as request/reply.</summary>
    public static T InOut<T>(this T steps, string uri)
        where T : RouteSteps => steps.To(ExchangePattern.InOut, uri);

    public static T Process<T>(this T steps, IProcessor processor)
        where T : RouteSteps
    {
        steps.AddStep(new ProcessDefinition(processor));
        return steps;
    }

    public static T Process<T>(this T steps, Action<Exchange> processor)
        where T : RouteSteps => steps.Process(DelegateProcessor.From(processor));

    public static T Process<T>(this T steps, Func<Exchange, Task> processor)
        where T : RouteSteps => steps.Process(DelegateProcessor.From(processor));

    /// <summary>Runs a processor looked up by name in the registry when the route starts.</summary>
    public static T ProcessRef<T>(this T steps, string name)
        where T : RouteSteps
    {
        steps.AddStep(new ProcessDefinition(new RegistryProcessor(name)) { Ref = name });
        return steps;
    }

    public static T SetBody<T>(this T steps, IExpression expression)
        where T : RouteSteps
    {
        steps.AddStep(new SetBodyDefinition(expression));
        return steps;
    }

    public static T SetBody<T>(this T steps, Func<Exchange, object?> expression)
        where T : RouteSteps => steps.SetBody(new FuncExpression(expression));

    /// <summary>Sets the body using a function of the current body converted to <typeparamref name="TIn"/>.</summary>
    public static T SetBody<T, TIn>(this T steps, Func<TIn, object?> expression)
        where T : RouteSteps => steps.SetBody(new FuncExpression(e => expression(e.Message.GetBody<TIn>()!)));

    public static T Transform<T>(this T steps, IExpression expression)
        where T : RouteSteps
    {
        steps.AddStep(new TransformDefinition(expression));
        return steps;
    }

    public static T Transform<T>(this T steps, Func<Exchange, object?> expression)
        where T : RouteSteps => steps.Transform(new FuncExpression(expression));

    /// <summary>Transforms the body converted to <typeparamref name="TIn"/> into a new body.</summary>
    public static T Transform<T, TIn, TOut>(this T steps, Func<TIn, TOut> transform)
        where T : RouteSteps => steps.Transform(new FuncExpression(e => transform(e.Message.GetBody<TIn>()!)));

    public static T SetHeader<T>(this T steps, string name, IExpression expression)
        where T : RouteSteps
    {
        steps.AddStep(new SetHeaderDefinition(name, expression));
        return steps;
    }

    public static T SetHeader<T>(this T steps, string name, Func<Exchange, object?> expression)
        where T : RouteSteps => steps.SetHeader(name, new FuncExpression(expression));

    /// <summary>
    /// Sets the id of the last step. When no step has been added yet on a route, sets the route id
    /// (like Camel's <c>from(...).id(...)</c>).
    /// </summary>
    public static T Id<T>(this T steps, string id)
        where T : RouteSteps
    {
        if (steps.Last is { } last)
        {
            last.Id = id;
        }
        else if (steps.Route is { } route && steps.Parent is null)
        {
            route.Id = id;
        }
        else
        {
            throw new InvalidOperationException("Id() must follow a step");
        }

        return steps;
    }

    /// <summary>Sets the description of the last step.</summary>
    public static T Description<T>(this T steps, string description)
        where T : RouteSteps
    {
        (steps.Last ?? throw new InvalidOperationException("Description() must follow a step")).Description = description;
        return steps;
    }

    /// <summary>Disables the last step (it is skipped when the route is built).</summary>
    public static T Disabled<T>(this T steps, bool disabled = true)
        where T : RouteSteps
    {
        (steps.Last ?? throw new InvalidOperationException("Disabled() must follow a step")).Disabled = disabled;
        return steps;
    }

    private sealed class RegistryProcessor(string name) : IProcessor
    {
        private IProcessor? _resolved;

        public ValueTask ProcessAsync(Exchange exchange)
        {
            _resolved ??= exchange.Context.Registry.Lookup(name) as IProcessor ?? throw new NoSuchBeanException(name);
            return _resolved.ProcessAsync(exchange);
        }
    }
}
