namespace Caravan.Spi;

/// <summary>Receives context events (Camel's <c>EventNotifier</c>).</summary>
public interface IEventNotifier
{
    /// <summary>Whether <see cref="Notify"/> should be called for <paramref name="evt"/> (default: always).</summary>
    bool IsEnabled(CaravanEvent evt) => true;

    /// <summary>Handles an event. Exceptions thrown here are ignored.</summary>
    void Notify(CaravanEvent evt);
}
