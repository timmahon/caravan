using Caravan.Model.Rest;

namespace Caravan.Spi;

/// <summary>
/// Implemented by components that can serve REST DSL services (Camel's <c>RestConsumerFactory</c>), e.g. <c>platform-http</c> on ASP.NET
/// Core. The <c>rest</c> component asks it for the consumer of each service when the service's route starts.
/// </summary>
public interface IRestConsumerFactory
{
    /// <summary>Creates the consumer of a REST service.</summary>
    /// <param name="context">The context.</param>
    /// <param name="processor">The route's processor.</param>
    /// <param name="method">The HTTP method in upper case.</param>
    /// <param name="basePath">The REST definition's path (without the configuration's context path).</param>
    /// <param name="uriTemplate">The verb's path with <c>{name}</c> parameters, or null.</param>
    /// <param name="consumes">The media types accepted, or null.</param>
    /// <param name="produces">The media types produced, or null.</param>
    /// <param name="configuration">The context's REST configuration (context path, CORS, endpoint properties...).</param>
    /// <param name="parameters">Further options of the <c>rest</c> endpoint, for the consumer.</param>
    /// <returns>The consumer; it is started and stopped with the route.</returns>
    IConsumer CreateConsumer(
        ICaravanContext context,
        IProcessor processor,
        string method,
        string basePath,
        string? uriTemplate,
        string? consumes,
        string? produces,
        RestConfiguration configuration,
        IReadOnlyDictionary<string, string> parameters);
}
