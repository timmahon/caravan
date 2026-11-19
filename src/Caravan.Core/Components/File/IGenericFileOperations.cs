namespace Caravan.Components.File;

/// <summary>
/// The file system operations of a file component (Camel's <c>GenericFileOperations</c>): the generic consumer, producer, process strategies
/// and read locks only use these, so a remote file component (FTP, SFTP, blob storage) reuses them by implementing this interface for its
/// file type. <see cref="FileOperations"/> is the local implementation. Paths are absolute, or relative to the current directory.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public interface IGenericFileOperations<T>
{
    /// <summary>Creates <paramref name="directory"/> and its parents; returns whether it exists afterwards.</summary>
    /// <param name="directory">The directory.</param>
    /// <param name="absolute">Whether the path is absolute.</param>
    bool BuildDirectory(string directory, bool absolute);

    /// <summary>Whether the file exists.</summary>
    /// <param name="name">The file path.</param>
    bool ExistsFile(string name);

    /// <summary>Whether the directory exists.</summary>
    /// <param name="directory">The directory path.</param>
    bool ExistsDirectory(string directory);

    /// <summary>Deletes the file; returns false when there was none.</summary>
    /// <param name="name">The file path.</param>
    /// <exception cref="GenericFileOperationFailedException">The file exists but could not be deleted.</exception>
    bool DeleteFile(string name);

    /// <summary>Renames (moves) a file; returns false when it could not be renamed (e.g. <paramref name="to"/> exists).</summary>
    /// <param name="from">The current path.</param>
    /// <param name="to">The new path.</param>
    bool RenameFile(string from, string to);

    /// <summary>
    /// Lists the entries of <paramref name="directory"/> (files and directories, not recursively), with their names relative to the
    /// endpoint's starting directory; empty when the directory does not exist (any more).
    /// </summary>
    /// <param name="directory">The directory.</param>
    IReadOnlyList<GenericFile<T>> ListFiles(string directory);

    /// <summary>The current state of one file (length, modification), or null when it does not exist.</summary>
    /// <param name="name">The file path.</param>
    GenericFile<T>? GetFile(string name);

    /// <summary>
    /// Makes the content of <paramref name="file"/> readable before the exchange is routed (sets <see cref="GenericFile{T}.ContentOpener"/>;
    /// a remote component may download it); returns false when the file cannot be retrieved.
    /// </summary>
    /// <param name="file">The file (the exchange's body).</param>
    /// <param name="exchange">The exchange.</param>
    /// <param name="cancellationToken">Cancels the retrieval.</param>
    ValueTask<bool> RetrieveFileAsync(GenericFile<T> file, Exchange exchange, CancellationToken cancellationToken);

    /// <summary>Releases what <see cref="RetrieveFileAsync"/> held for the exchange (local copies, connections) once it is done.</summary>
    /// <param name="exchange">The exchange.</param>
    void ReleaseRetrievedFileResources(Exchange exchange);

    /// <summary>
    /// Writes the body of the exchange to <paramref name="name"/>, streaming it (a <see cref="Stream"/>, a consumed file, bytes or text in the
    /// endpoint's charset); returns whether it was written.
    /// </summary>
    /// <param name="name">The file path.</param>
    /// <param name="exchange">The exchange.</param>
    /// <param name="append">Whether to append to an existing file rather than replace it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="GenericFileOperationFailedException">The body is null (and null bodies are not allowed) or could not be written.</exception>
    ValueTask<bool> StoreFileAsync(string name, Exchange exchange, bool append, CancellationToken cancellationToken);
}
