using System.Reflection;
using Caravan.Model;

namespace Caravan.Builder;

/// <summary>DSL for the <c>throwException</c> EIP.</summary>
public static class ThrowExceptionDsl
{
    /// <summary>Fails the exchange with <paramref name="exception"/> (the same instance every time).</summary>
    public static T ThrowException<T>(this T steps, Exception exception)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(exception);
        steps.AddStep(new ThrowExceptionDefinition(_ => exception) { ExceptionType = exception.GetType() });
        return steps;
    }

    /// <summary>Fails the exchange with an exception created for it: <c>.ThrowException(e =&gt; new IOException("..."))</c>.</summary>
    public static T ThrowException<T>(this T steps, Func<Exchange, Exception> factory)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(factory);
        steps.AddStep(new ThrowExceptionDefinition(factory));
        return steps;
    }

    /// <summary>
    /// Fails the exchange with a new <paramref name="exceptionType"/> created with <paramref name="message"/> (Camel's
    /// <c>throwException(Class, String)</c>): <c>.ThrowException(typeof(IOException), "Forced")</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The type is not an exception with a constructor taking a message.</exception>
    public static T ThrowException<T>(this T steps, Type exceptionType, string? message)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        if (!typeof(Exception).IsAssignableFrom(exceptionType))
        {
            throw new ArgumentException($"{exceptionType} is not an exception type", nameof(exceptionType));
        }

        var constructor = exceptionType.GetConstructor([typeof(string)])
            ?? throw new ArgumentException($"{exceptionType} has no public constructor taking a message", nameof(exceptionType));
        steps.AddStep(new ThrowExceptionDefinition(_ => Create(constructor, message)) { ExceptionType = exceptionType });
        return steps;
    }

    private static Exception Create(ConstructorInfo constructor, string? message)
    {
        try
        {
            return (Exception)constructor.Invoke([message]);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            return e.InnerException;
        }
    }
}
