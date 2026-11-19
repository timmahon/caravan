namespace Caravan.Spi;

/// <summary>
/// How the properties component maps property keys to environment variables (Camel's <c>environmentVariableMode</c>): the key
/// <c>app.queue-name</c> is looked up as <c>APP_QUEUE_NAME</c>.
/// </summary>
public enum EnvironmentVariableMode
{
    /// <summary>Environment variables are only read with an explicit <c>{{env:NAME}}</c> placeholder (the default).</summary>
    Never,

    /// <summary>Environment variables are consulted when no source defines the key.</summary>
    Fallback,

    /// <summary>Environment variables take precedence over every source except the override properties.</summary>
    Override,
}
