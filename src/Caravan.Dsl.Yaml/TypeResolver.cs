namespace Caravan.Dsl.Yaml;

/// <summary>
/// Resolves type names written in YAML: .NET names (full, assembly-qualified, C# keywords, unique simple names, as the Simple language does)
/// and the Java names of common types and exceptions found in Camel routes (<c>java.lang.IllegalArgumentException</c> →
/// <see cref="ArgumentException"/>). A <c>#class:</c> prefix is ignored.
/// </summary>
internal static class TypeResolver
{
    private static readonly Dictionary<string, Type> JavaTypes = new(StringComparer.Ordinal)
    {
        ["java.lang.Throwable"] = typeof(Exception),
        ["java.lang.Exception"] = typeof(Exception),
        ["Throwable"] = typeof(Exception),
        ["java.lang.RuntimeException"] = typeof(Exception),
        ["RuntimeException"] = typeof(Exception),
        ["java.lang.IllegalArgumentException"] = typeof(ArgumentException),
        ["IllegalArgumentException"] = typeof(ArgumentException),
        ["java.lang.IllegalStateException"] = typeof(InvalidOperationException),
        ["IllegalStateException"] = typeof(InvalidOperationException),
        ["java.lang.NullPointerException"] = typeof(NullReferenceException),
        ["java.lang.UnsupportedOperationException"] = typeof(NotSupportedException),
        ["java.lang.ArithmeticException"] = typeof(ArithmeticException),
        ["java.lang.NumberFormatException"] = typeof(FormatException),
        ["java.lang.IndexOutOfBoundsException"] = typeof(IndexOutOfRangeException),
        ["java.lang.InterruptedException"] = typeof(OperationCanceledException),
        ["java.io.IOException"] = typeof(IOException),
        ["IOException"] = typeof(IOException),
        ["java.io.FileNotFoundException"] = typeof(FileNotFoundException),
        ["java.util.concurrent.TimeoutException"] = typeof(TimeoutException),
        ["java.net.ConnectException"] = typeof(System.Net.Sockets.SocketException),
        ["java.net.SocketException"] = typeof(System.Net.Sockets.SocketException),
        ["org.apache.camel.CamelException"] = typeof(CaravanException),
        ["org.apache.camel.CamelExchangeException"] = typeof(CaravanExchangeException),
        ["org.apache.camel.CamelExecutionException"] = typeof(CaravanExecutionException),
        ["org.apache.camel.ValidationException"] = typeof(PredicateValidationException),
        ["org.apache.camel.support.processor.PredicateValidationException"] = typeof(PredicateValidationException),
        ["org.apache.camel.processor.validation.PredicateValidationException"] = typeof(PredicateValidationException),
        ["org.apache.camel.InvalidPayloadException"] = typeof(InvalidPayloadException),
        ["org.apache.camel.ExchangeTimedOutException"] = typeof(ExchangeTimedOutException),
        ["org.apache.camel.RollbackExchangeException"] = typeof(RollbackExchangeException),
        ["java.lang.String"] = typeof(string),
        ["java.lang.Integer"] = typeof(int),
        ["java.lang.Long"] = typeof(long),
        ["java.lang.Double"] = typeof(double),
        ["java.lang.Boolean"] = typeof(bool),
        ["java.math.BigDecimal"] = typeof(decimal),
        ["byte[]"] = typeof(byte[]),
    };

    /// <summary>The type named <paramref name="name"/>, or null.</summary>
    public static Type? Resolve(ICaravanContext context, string name)
    {
        name = name.Trim();
        if (name.StartsWith("#class:", StringComparison.Ordinal))
        {
            name = name["#class:".Length..].Trim();
        }

        if (name.Length == 0)
        {
            return null;
        }

        if (JavaTypes.TryGetValue(name, out var type))
        {
            return type;
        }

        try
        {
            return context.TypeConverter.Convert(typeof(Type), null, name) as Type;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
