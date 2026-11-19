using Caravan.Components.Bean;
using Caravan.Spi;

namespace Caravan.Processors;

/// <summary>
/// The <c>bean</c> EIP and the <c>bean</c>/<c>class</c> components (Camel's <c>BeanProcessor</c>): calls a method of the bean chosen by Camel's
/// bean binding (see <c>docs/ARCHITECTURE.md</c>, "Bean binding") and sets the method's result as the body, whatever the exchange pattern
/// (a <c>void</c>, <see cref="Task"/> or <see cref="ValueTask"/> method leaves the body unchanged). A bean that is an <see cref="IProcessor"/>
/// called without a method name processes the exchange itself.
/// </summary>
public sealed class BeanProcessor : IProcessor
{
    private readonly BeanHolder _holder;

    internal BeanProcessor(BeanHolder holder, string? method)
    {
        _holder = holder;
        Method = string.IsNullOrWhiteSpace(method) ? null : method;
        if (Method is not null)
        {
            // Fails early on a malformed method specification.
            BeanMethodSpec.Parse(Method);
        }
    }

    /// <summary>The method specification (<c>name</c>, <c>name(types)</c> or <c>name(values)</c>), or null to choose by the body.</summary>
    public string? Method { get; }

    /// <inheritdoc />
    public async ValueTask ProcessAsync(Exchange exchange)
    {
        var (hasResult, result) = await InvokeAsync(_holder, Method, exchange).ConfigureAwait(false);
        if (hasResult)
        {
            exchange.Message.Body = result;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Method is null ? $"Bean[{_holder}]" : $"Bean[{_holder}.{Method}]";

    /// <summary>
    /// Looks a singleton registry bean up and validates that the method exists on the bean type when it is known, so that a missing bean or
    /// a wrong method name fails route creation.
    /// </summary>
    /// <exception cref="NoSuchBeanException">The registry bean is missing.</exception>
    /// <exception cref="MethodNotFoundException">No method matches.</exception>
    internal static void Validate(ICaravanContext context, BeanHolder holder, string? method)
    {
        var bean = holder.Type is null ? holder.GetSingleton(context) : null;
        if (string.IsNullOrWhiteSpace(method))
        {
            return;
        }

        var type = bean?.GetType() ?? holder.Type;
        if (type is not null && bean is not IProcessor)
        {
            BeanInfo.For(type).GetSelector(method, staticOnly: bean is null && holder.Type is not null && BeanActivator.IsStatic(holder.Type));
        }
    }

    /// <summary>Invokes the bean method chosen for <paramref name="exchange"/>.</summary>
    internal static async ValueTask<(bool HasResult, object? Result)> InvokeAsync(BeanHolder holder, string? method, Exchange exchange)
    {
        var bean = holder.GetBean(exchange);
        if (bean is IProcessor processor && string.IsNullOrWhiteSpace(method))
        {
            await processor.ProcessAsync(exchange).ConfigureAwait(false);
            return (false, null);
        }

        var type = bean?.GetType() ?? holder.Type ?? throw new InvalidOperationException($"No bean for {holder}");
        var invoker = BeanInfo.For(type).GetSelector(string.IsNullOrWhiteSpace(method) ? null : method, staticOnly: bean is null).Choose(exchange);
        return await invoker.InvokeAsync(bean, exchange).ConfigureAwait(false);
    }
}
