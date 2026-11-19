namespace Caravan;

/// <summary>Base class for Caravan exceptions.</summary>
public class CaravanException : Exception
{
    /// <summary>Creates an exception without a message.</summary>
    public CaravanException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public CaravanException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    public CaravanException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
