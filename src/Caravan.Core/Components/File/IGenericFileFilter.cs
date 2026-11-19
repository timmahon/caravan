namespace Caravan.Components.File;

/// <summary>Chooses the files (and directories, with <c>recursive</c>) a file consumer takes (Camel's <c>GenericFileFilter</c>; <c>filter=#bean</c>).</summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public interface IGenericFileFilter<T>
{
    /// <summary>Whether to accept <paramref name="file"/>; rejecting a directory skips its content.</summary>
    /// <param name="file">The file or directory.</param>
    bool Accept(GenericFile<T> file);
}
