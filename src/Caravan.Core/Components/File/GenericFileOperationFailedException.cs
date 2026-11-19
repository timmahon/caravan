namespace Caravan.Components.File;

/// <summary>A file operation failed (Camel's <c>GenericFileOperationFailedException</c>).</summary>
public class GenericFileOperationFailedException : CaravanException
{
    /// <summary>Creates an exception without a message.</summary>
    public GenericFileOperationFailedException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public GenericFileOperationFailedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and its cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public GenericFileOperationFailedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
