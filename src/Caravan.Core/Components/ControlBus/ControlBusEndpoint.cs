using Caravan.Spi;
using Caravan.Support;

namespace Caravan.Components.ControlBus;

/// <summary>A <c>controlbus</c> endpoint (see <see cref="ControlBusComponent"/>).</summary>
public sealed class ControlBusEndpoint : EndpointBase
{
    internal ControlBusEndpoint(string uri, ControlBusComponent component, string command, string? language)
        : base(uri, component, component.Context!)
    {
        Command = command;
        Language = language;
    }

    /// <summary><c>route</c> or <c>language</c>.</summary>
    public string Command { get; }

    /// <summary>The language name of a <c>language</c> endpoint.</summary>
    public string? Language { get; }

    /// <summary>The route id (<c>current</c> for the exchange's route); without it <c>status</c> and <c>stats</c> refer to the context.</summary>
    public string? RouteId { get; set; }

    /// <summary>The action: <c>start</c>, <c>stop</c>, <c>suspend</c>, <c>resume</c>, <c>restart</c>, <c>status</c> or <c>stats</c>.</summary>
    public string? Action { get; set; }

    /// <summary>Run the action in the background (default false); nothing is returned then.</summary>
    public bool Async { get; set; }

    /// <summary>The delay between stopping and starting the route with <c>restart</c> (default 1s), on the context's clock.</summary>
    public TimeSpan RestartDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public override IProducer CreateProducer() => new ControlBusProducer(this);

    /// <inheritdoc />
    public override IConsumer CreateConsumer(IProcessor processor) =>
        throw new NotSupportedException("The controlbus component does not support consumers");

    /// <inheritdoc />
    protected override Task DoStartAsync(CancellationToken cancellationToken)
    {
        if (Command == "route")
        {
            ControlBusProducer.ValidateAction(Action);
        }

        return Task.CompletedTask;
    }
}
