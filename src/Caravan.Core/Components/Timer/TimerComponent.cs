using Caravan.Support;

namespace Caravan.Components.Timer;

/// <summary>
/// The <c>timer</c> component: fires an empty exchange (<c>InOnly</c>) periodically, using the context's
/// <see cref="ICaravanContext.TimeProvider"/>.
/// <para>URI: <c>timer:name[?period=1000&amp;delay=1000&amp;repeatCount=0&amp;fixedRate=false&amp;includeMetadata=false]</c></para>
/// </summary>
public sealed class TimerComponent : ComponentBase
{
    /// <inheritdoc />
    protected override EndpointBase CreateEndpoint(string uri, string remaining, IDictionary<string, string> parameters)
    {
        if (string.IsNullOrEmpty(remaining))
        {
            throw new ArgumentException("timer endpoint requires a name, e.g. timer:tick");
        }

        return new TimerEndpoint(uri, this, remaining);
    }
}
