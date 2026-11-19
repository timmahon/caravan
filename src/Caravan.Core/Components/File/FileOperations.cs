using System.Text;
using Caravan.Spi;

namespace Caravan.Components.File;

/// <summary>
/// The local file system operations of the <c>file</c> component (Camel's <c>FileOperations</c>). Bodies are written by streaming copies
/// (a consumed file, a <see cref="Stream"/>), never loaded whole; consumed files are read lazily through <see cref="Open"/>.
/// </summary>
public sealed class FileOperations : IGenericFileOperations<FileInfo>
{
    private const int BufferSize = 81920;
    private readonly FileEndpoint _endpoint;

    /// <summary>Creates the operations of <paramref name="endpoint"/>.</summary>
    /// <param name="endpoint">The endpoint.</param>
    public FileOperations(FileEndpoint endpoint)
    {
        _endpoint = endpoint;
    }

    /// <summary>Opens a file for reading, sharing it with writers and deletions (the consumed file can still be moved meanwhile).</summary>
    /// <param name="path">The file.</param>
    public static Stream Open(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <inheritdoc />
    public bool BuildDirectory(string directory, bool absolute)
    {
        if (Directory.Exists(directory))
        {
            return true;
        }

        try
        {
            var created = Directory.CreateDirectory(directory);
            if (_endpoint.ChmodDirectoryMode is { } mode && !OperatingSystem.IsWindows())
            {
                System.IO.File.SetUnixFileMode(created.FullName, mode);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Directory.Exists(directory);
        }
    }

    /// <inheritdoc />
    public bool ExistsFile(string name) => System.IO.File.Exists(name);

    /// <inheritdoc />
    public bool ExistsDirectory(string directory) => Directory.Exists(directory);

    /// <inheritdoc />
    public bool DeleteFile(string name)
    {
        if (!System.IO.File.Exists(name))
        {
            return false;
        }

        try
        {
            System.IO.File.Delete(name);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new GenericFileOperationFailedException($"Cannot delete file: {name}", e);
        }
    }

    /// <inheritdoc />
    public bool RenameFile(string from, string to)
    {
        try
        {
            System.IO.File.Move(from, to, overwrite: false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GenericFile<FileInfo>> ListFiles(string directory)
    {
        try
        {
            return [.. new DirectoryInfo(directory).EnumerateFileSystemInfos().Select(AsGenericFile)];
        }
        catch (DirectoryNotFoundException)
        {
            // Deleted while polling.
            return [];
        }
    }

    /// <inheritdoc />
    public GenericFile<FileInfo>? GetFile(string name)
    {
        var info = new FileInfo(name);
        return info.Exists ? AsGenericFile(info) : null;
    }

    /// <inheritdoc />
    public ValueTask<bool> RetrieveFileAsync(GenericFile<FileInfo> file, Exchange exchange, CancellationToken cancellationToken)
    {
        // Nothing is read here: the body opens the file when it is converted.
        file.ContentOpener ??= static f => Open(f.AbsoluteFilePath);
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc />
    public void ReleaseRetrievedFileResources(Exchange exchange)
    {
    }

    /// <inheritdoc />
    public async ValueTask<bool> StoreFileAsync(string name, Exchange exchange, bool append, CancellationToken cancellationToken)
    {
        var body = exchange.Message.Body;
        if (body is null && !_endpoint.AllowNullBody)
        {
            throw new GenericFileOperationFailedException($"Cannot write null body to file: {name}");
        }

        var encoding = _endpoint.Charset is { } charset ? GetEncoding(charset) : null;
        try
        {
            var output = new FileStream(name, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, FileOptions.Asynchronous);
            await using (output.ConfigureAwait(false))
            {
                if (body is not null)
                {
                    await WriteBodyAsync(output, body, exchange, encoding, cancellationToken).ConfigureAwait(false);
                    if (_endpoint.AppendChars is { Length: > 0 } appendChars)
                    {
                        await output.WriteAsync((encoding ?? Utf8).GetBytes(appendChars), cancellationToken).ConfigureAwait(false);
                    }
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (_endpoint.ForceWrites)
                {
                    output.Flush(flushToDisk: true);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new GenericFileOperationFailedException($"Cannot store file: {name}", e);
        }

        if (_endpoint.KeepLastModified && LastModifiedOf(exchange.Message.GetHeader(CaravanHeaders.FileLastModified)) is { } lastModified)
        {
            System.IO.File.SetLastWriteTimeUtc(name, lastModified);
        }

        if (_endpoint.ChmodMode is { } mode && !OperatingSystem.IsWindows())
        {
            System.IO.File.SetUnixFileMode(name, mode);
        }

        return true;
    }

    private static Encoding Utf8 { get; } = new UTF8Encoding(false);

    private static Encoding GetEncoding(string charset)
    {
        var encoding = Encoding.GetEncoding(charset);
        return encoding is UTF8Encoding ? Utf8 : encoding;
    }

    private static DateTime? LastModifiedOf(object? value) => value switch
    {
        long ms => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime,
        int ms => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime,
        DateTimeOffset d => d.UtcDateTime,
        DateTime d => d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc),
        string s when long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var ms) =>
            DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime,
        _ => null,
    };

    private static async Task WriteBodyAsync(Stream output, object body, Exchange exchange, Encoding? encoding, CancellationToken cancellationToken)
    {
        switch (body)
        {
            case IGenericFile file when encoding is not null && file.Charset is { } sourceCharset && !GetEncoding(sourceCharset).Equals(encoding):
            {
                // Transcode text from the consumed file's charset to ours, streaming.
                using var reader = new StreamReader(file.OpenRead(), GetEncoding(sourceCharset), detectEncodingFromByteOrderMarks: false, BufferSize);
                var writer = new StreamWriter(output, encoding, BufferSize, leaveOpen: true);
                await using (writer.ConfigureAwait(false))
                {
                    var buffer = new char[BufferSize / 2];
                    int read;
                    while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await writer.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                }

                break;
            }

            case IGenericFile file:
            {
                var input = file.OpenRead();
                await using (input.ConfigureAwait(false))
                {
                    await input.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
                }

                break;
            }

            case IStreamCache cache:
                cache.Reset();
                await ((Stream)cache).CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
                cache.Reset();
                break;
            case Stream stream:
                await using (stream.ConfigureAwait(false))
                {
                    await stream.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
                }

                break;
            case byte[] bytes:
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                break;
            case ReadOnlyMemory<byte> memory:
                await output.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                break;
            case string text:
                await output.WriteAsync((encoding ?? Utf8).GetBytes(text), cancellationToken).ConfigureAwait(false);
                break;
            default:
                if (exchange.Context.TypeConverter.TryConvert(typeof(Stream), exchange, body, out var converted) && converted is Stream s)
                {
                    await WriteBodyAsync(output, s, exchange, encoding, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var str = exchange.Context.TypeConverter.Convert(typeof(string), exchange, body) as string ?? string.Empty;
                    await output.WriteAsync((encoding ?? Utf8).GetBytes(str), cancellationToken).ConfigureAwait(false);
                }

                break;
        }
    }

    private GenericFile<FileInfo> AsGenericFile(FileSystemInfo info)
    {
        var file = info as FileInfo ?? new FileInfo(info.FullName);
        var isDirectory = info is DirectoryInfo;
        var relative = Path.GetRelativePath(_endpoint.AbsoluteDirectory, info.FullName);
        return new GenericFile<FileInfo>(file)
        {
            EndpointPath = _endpoint.Directory,
            AbsoluteFilePath = info.FullName,
            RelativeFilePath = relative,
            FileName = relative,
            FileNameOnly = info.Name,
            FileLength = isDirectory ? 0 : file.Length,
            LastModified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            IsDirectory = isDirectory,
            IsAbsolute = Path.IsPathRooted(_endpoint.Directory),
            Charset = _endpoint.Charset,
        };
    }
}
