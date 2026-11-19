namespace Caravan.Components.File;

/// <summary>
/// The non-generic view of a <see cref="GenericFile{T}"/>, used by the type converters (a file body converts to <see cref="Stream"/>,
/// <c>byte[]</c> and <see cref="string"/>) and by producers writing a file that was consumed.
/// </summary>
public interface IGenericFile
{
    /// <summary>The starting directory of the endpoint that listed the file, as configured.</summary>
    string EndpointPath { get; }

    /// <summary>The name relative to the starting directory (<c>sub/hello.txt</c>).</summary>
    string FileName { get; }

    /// <summary>The name without leading directories (<c>hello.txt</c>).</summary>
    string FileNameOnly { get; }

    /// <summary>The path relative to the starting directory.</summary>
    string RelativeFilePath { get; }

    /// <summary>The absolute path.</summary>
    string AbsoluteFilePath { get; }

    /// <summary>The directory holding the file (starting with the starting directory as configured, or absolute), or null.</summary>
    string? Parent { get; }

    /// <summary>The length in bytes.</summary>
    long FileLength { get; }

    /// <summary>When the file was last modified, in Unix milliseconds.</summary>
    long LastModified { get; }

    /// <summary>Whether this is a directory.</summary>
    bool IsDirectory { get; }

    /// <summary>Whether the path is absolute (the starting directory was configured as an absolute path).</summary>
    bool IsAbsolute { get; }

    /// <summary>The charset of the content when read as text (the endpoint's <c>charset</c>), or null for UTF-8.</summary>
    string? Charset { get; }

    /// <summary>The underlying file object (e.g. a <see cref="FileInfo"/>).</summary>
    object? FileObject { get; }

    /// <summary>Opens the content for reading; the caller disposes the stream. Nothing is read before.</summary>
    /// <exception cref="InvalidOperationException">The content has not been retrieved by the file operations.</exception>
    Stream OpenRead();
}
