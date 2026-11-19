namespace Caravan.Components.File;

/// <summary>
/// A <c>file</c> endpoint: <see cref="GenericFileEndpoint{T}"/> over the local file system (<see cref="FileInfo"/>), adding the
/// <c>markerFile</c>, <c>changed</c> and <c>fileLock</c> read locks and the POSIX permissions of written files and created directories.
/// </summary>
public sealed class FileEndpoint : GenericFileEndpoint<FileInfo>
{
    private string? _chmod;
    private string? _chmodDirectory;

    /// <summary>Creates the endpoint.</summary>
    /// <param name="endpointUri">The endpoint URI.</param>
    /// <param name="component">The component.</param>
    /// <param name="directory">The starting directory.</param>
    public FileEndpoint(string endpointUri, FileComponent component, string directory)
        : base(endpointUri, component, component.Context!, directory)
    {
    }

    /// <summary>The permissions of written files, in octal (<c>640</c>); ignored on Windows.</summary>
    public string? Chmod
    {
        get => _chmod;
        set
        {
            _chmod = value;
            ChmodMode = ParseMode(value);
        }
    }

    /// <summary>The parsed <see cref="Chmod"/>.</summary>
    public UnixFileMode? ChmodMode { get; private set; }

    /// <summary>The permissions of directories the producer creates, in octal (<c>750</c>); ignored on Windows.</summary>
    public string? ChmodDirectory
    {
        get => _chmodDirectory;
        set
        {
            _chmodDirectory = value;
            ChmodDirectoryMode = ParseMode(value);
        }
    }

    /// <summary>The parsed <see cref="ChmodDirectory"/>.</summary>
    public UnixFileMode? ChmodDirectoryMode { get; private set; }

    /// <inheritdoc />
    protected internal override IGenericFileOperations<FileInfo> CreateOperations() => new FileOperations(this);

    /// <inheritdoc />
    protected override IGenericFileExclusiveReadLockStrategy<FileInfo>? CreateExclusiveReadLockStrategy()
    {
        switch (ReadLock.ToLowerInvariant())
        {
            case "markerfile":
                return Marker(null);
            case "changed":
                return WithMarker(new FileChangedExclusiveReadLockStrategy
                {
                    CheckInterval = ReadLockCheckInterval,
                    Timeout = ReadLockTimeout,
                    MinLength = ReadLockMinLength,
                    MinAge = ReadLockMinAge,
                    ReadLockLoggingLevel = ReadLockLoggingLevel,
                });
            case "filelock":
                return WithMarker(new FileLockExclusiveReadLockStrategy
                {
                    CheckInterval = ReadLockCheckInterval,
                    Timeout = ReadLockTimeout,
                    ReadLockLoggingLevel = ReadLockLoggingLevel,
                });
            case "rename":
                return WithMarker(base.CreateExclusiveReadLockStrategy()!);
            default:
                return base.CreateExclusiveReadLockStrategy();
        }
    }

    private static UnixFileMode? ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var mode = Convert.ToInt32(value.Trim(), 8);
            return mode is >= 0 and <= 0xFFF ? (UnixFileMode)mode : throw new FormatException();
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            throw new ArgumentException($"Invalid permissions '{value}': use octal digits such as 644", e);
        }
    }

    private IGenericFileExclusiveReadLockStrategy<FileInfo> WithMarker(IGenericFileExclusiveReadLockStrategy<FileInfo> inner) =>
        ReadLockMarkerFile ? Marker(inner) : inner;

    private MarkerFileExclusiveReadLockStrategy Marker(IGenericFileExclusiveReadLockStrategy<FileInfo>? inner) => new(inner)
    {
        DeleteOrphanLockFiles = ReadLockDeleteOrphanLockFiles,
        ReadLockLoggingLevel = ReadLockLoggingLevel,
    };
}
