using Caravan.Model;
using Caravan.Support;
using Microsoft.Extensions.Logging;

namespace Caravan.Builder;

/// <summary>DSL for the <c>log</c> EIP (not to be confused with the <c>log:</c> component).</summary>
public static class LogDsl
{
    /// <summary>
    /// Logs <paramref name="message"/> at <c>Information</c> with the route id as logger name. The message may contain
    /// <c>${...}</c> tokens (see <see cref="TokenTemplate"/>), e.g. <c>.Log("Got ${body}")</c>.
    /// </summary>
    public static T Log<T>(this T steps, string message)
        where T : RouteSteps => steps.Log(LogLevel.Information, message);

    /// <summary>Logs <paramref name="message"/> at <paramref name="level"/> with the route id as logger name.</summary>
    public static T Log<T>(this T steps, LogLevel level, string message)
        where T : RouteSteps
    {
        steps.AddStep(new LogDefinition(message) { LoggingLevel = level });
        return steps;
    }

    /// <summary>Logs <paramref name="message"/> at <paramref name="level"/> with the logger (category) <paramref name="loggerName"/>.</summary>
    public static T Log<T>(this T steps, LogLevel level, string loggerName, string message)
        where T : RouteSteps
    {
        steps.AddStep(new LogDefinition(message) { LoggingLevel = level, LogName = loggerName });
        return steps;
    }
}
