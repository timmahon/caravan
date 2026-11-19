using Caravan.Spi;

namespace Caravan.Testing.Notify;

/// <summary>Counts exchanges received, done, completed or failed (Camel's <c>whenReceived</c>, <c>whenDone</c>, <c>whenExactlyDone</c>, ...).</summary>
internal sealed class CountNotifyPredicate(NotifyCountKind kind, int number, bool exact) : NotifyEventPredicate
{
    private int _count;

    public override bool OnCreated(Exchange exchange, CaravanEvent evt)
    {
        if (kind == NotifyCountKind.Received)
        {
            _count++;
        }

        return true;
    }

    public override bool OnCompleted(Exchange exchange, CaravanEvent evt)
    {
        if (kind is NotifyCountKind.Done or NotifyCountKind.Completed)
        {
            _count++;
        }

        return true;
    }

    public override bool OnFailed(Exchange exchange, CaravanEvent evt)
    {
        if (kind is NotifyCountKind.Done or NotifyCountKind.Failed)
        {
            _count++;
        }

        return true;
    }

    public override bool Matches() => exact ? _count == number : _count >= number;

    public override void Reset() => _count = 0;

    public override string ToString() => $"when{(exact ? "Exactly" : string.Empty)}{kind}({number})";
}
