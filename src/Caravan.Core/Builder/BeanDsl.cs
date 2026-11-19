using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>bean</c> EIP.</summary>
public static class BeanDsl
{
    /// <summary>
    /// Calls a method of <paramref name="bean"/>; its result becomes the body. Without <paramref name="method"/> the method is chosen by the
    /// body (see <c>docs/ARCHITECTURE.md</c>, "Bean binding"), e.g. <c>.Bean(new Greeter(), "Greet(${body}, ${header.lang})")</c>.
    /// </summary>
    /// <typeparam name="T">The block type.</typeparam>
    /// <param name="steps">The block to add to.</param>
    /// <param name="bean">The bean.</param>
    /// <param name="method">The method specification.</param>
    /// <returns>The block.</returns>
    public static T Bean<T>(this T steps, object bean, string? method = null)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(bean);
        steps.AddStep(bean is Type type ? new BeanDefinition { BeanType = type, Method = method } : new BeanDefinition { Bean = bean, Method = method });
        return steps;
    }

    /// <summary>
    /// Calls a method of a bean of type <paramref name="beanType"/>, taken from the context's service provider or created (constructor
    /// parameters from the service provider), kept according to <paramref name="scope"/>; a static class's static methods are called.
    /// </summary>
    /// <typeparam name="T">The block type.</typeparam>
    /// <param name="steps">The block to add to.</param>
    /// <param name="beanType">The bean type.</param>
    /// <param name="method">The method specification.</param>
    /// <param name="scope">How long the bean is kept.</param>
    /// <returns>The block.</returns>
    public static T Bean<T>(this T steps, Type beanType, string? method = null, BeanScope scope = BeanScope.Singleton)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(beanType);
        steps.AddStep(new BeanDefinition { BeanType = beanType, Method = method, Scope = scope });
        return steps;
    }

    /// <summary>Calls a method of the registry bean <paramref name="beanName"/> (Camel's <c>bean("name", "method")</c>).</summary>
    /// <typeparam name="T">The block type.</typeparam>
    /// <param name="steps">The block to add to.</param>
    /// <param name="beanName">The registry name (property placeholders allowed).</param>
    /// <param name="method">The method specification.</param>
    /// <param name="scope">
    /// <see cref="BeanScope.Singleton"/> looks the bean up when the route is created, <see cref="BeanScope.Request"/> once per exchange,
    /// <see cref="BeanScope.Prototype"/> on every call.
    /// </param>
    /// <returns>The block.</returns>
    public static T Bean<T>(this T steps, string beanName, string? method = null, BeanScope scope = BeanScope.Singleton)
        where T : RouteSteps
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(beanName);
        steps.AddStep(new BeanDefinition { Ref = beanName, Method = method, Scope = scope });
        return steps;
    }
}
