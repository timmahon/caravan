namespace Caravan;

/// <summary>Base class for Caravan exceptions.</summary>
public class CaravanException : Exception
{
    public CaravanException()
    {
    }

    public CaravanException(string message)
        : base(message)
    {
    }

    public CaravanException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>An exception tied to the exchange being processed.</summary>
public class CaravanExchangeException : CaravanException
{
    public CaravanExchangeException(string message, Exchange? exchange, Exception? innerException = null)
        : base(exchange is null ? message : $"{message}. Exchange[{exchange.ExchangeId}]", innerException)
    {
        Exchange = exchange;
    }

    public Exchange? Exchange { get; }
}

/// <summary>Thrown by <see cref="IProducerTemplate"/> when routing an exchange failed.</summary>
public class CaravanExecutionException(string message, Exchange exchange, Exception? innerException)
    : CaravanExchangeException(message, exchange, innerException);

/// <summary>The body was missing or could not be converted to the required type.</summary>
public class InvalidPayloadException(Exchange exchange, Type type)
    : CaravanExchangeException($"No body available of type: {type.FullName}", exchange)
{
    public Type Type { get; } = type;
}

/// <summary>An endpoint URI could not be resolved.</summary>
public class ResolveEndpointFailedException(string uri, string message, Exception? innerException = null)
    : CaravanException($"Failed to resolve endpoint: {uri} due to: {message}", innerException)
{
    public string Uri { get; } = uri;
}

/// <summary>No component is registered for the URI scheme.</summary>
public class NoSuchEndpointException(string uri, string message)
    : CaravanException($"No endpoint could be found for: {uri}, please check your classpath contains the needed Caravan component package. {message}")
{
    public string Uri { get; } = uri;
}

/// <summary>A bean could not be found in the registry.</summary>
public class NoSuchBeanException(string name)
    : CaravanException($"No bean could be found in the registry for: {name}")
{
    public string Name { get; } = name;
}

/// <summary>A route definition is invalid.</summary>
public class FailedToCreateRouteException(string routeId, string message, Exception? innerException = null)
    : CaravanException($"Failed to create route {routeId}: {message}", innerException)
{
    public string RouteId { get; } = routeId;
}

/// <summary>A route failed to start.</summary>
public class FailedToStartRouteException(string routeId, string message, Exception? innerException = null)
    : CaravanException($"Failed to start route {routeId} because of {message}", innerException)
{
    public string RouteId { get; } = routeId;
}
