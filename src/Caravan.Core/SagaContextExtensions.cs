using Caravan.Saga;

namespace Caravan;

/// <summary>Chooses the <see cref="ISagaService"/> the <c>saga</c> EIP and the <c>saga</c> component of a context use.</summary>
public static class SagaContextExtensions
{
    /// <summary>Sets the saga service of the context. Set it before routes using the <c>saga</c> EIP are created.</summary>
    /// <param name="context">The context.</param>
    /// <param name="service">The service.</param>
    public static void SetSagaService(this ICaravanContext context, ISagaService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        context.SetExtension(service);
    }

    /// <summary>
    /// The saga service of the context: the one set with <see cref="SetSagaService"/>, else the only <see cref="ISagaService"/> bound in the
    /// registry, else an <see cref="InMemorySagaService"/> created on first use (Camel's default).
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The service.</returns>
    public static ISagaService GetSagaService(this ICaravanContext context) =>
        context.GetExtension<ISagaService>()
        ?? context.Registry.FindSingleByType<ISagaService>()
        ?? context.GetOrAddExtension<ISagaService>(() => new InMemorySagaService(context));
}
