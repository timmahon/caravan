namespace Caravan.Components.File;

/// <summary>
/// A file listed by a file component (Camel's <c>GenericFile</c>): its names and paths, length, last modification and the underlying object
/// (<see cref="FileInfo"/> for the <c>file</c> component; a remote entry for future FTP/SFTP/blob components). It is the body of the exchanges
/// a <see cref="GenericFileConsumer{T}"/> creates. The content is read only when the body is converted (to <see cref="Stream"/>,
/// <c>byte[]</c> or <see cref="string"/>) or <see cref="OpenRead"/> is called: <see cref="ContentOpener"/>, set when the file operations
/// retrieve the file, opens it.
/// </summary>
/// <typeparam name="T">The type of the underlying file object.</typeparam>
public sealed class GenericFile<T> : IGenericFile
{
    /// <summary>Creates a file entry.</summary>
    /// <param name="file">The underlying file object.</param>
    public GenericFile(T file)
    {
        File = file;
    }

    /// <summary>The underlying file object.</summary>
    public T File { get; set; }

    /// <inheritdoc />
    public string EndpointPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public string FileName { get; set; } = string.Empty;

    /// <inheritdoc />
    public string FileNameOnly { get; set; } = string.Empty;

    /// <inheritdoc />
    public string RelativeFilePath { get; set; } = string.Empty;

    /// <inheritdoc />
    public string AbsoluteFilePath { get; set; } = string.Empty;

    /// <inheritdoc />
    public long FileLength { get; set; }

    /// <inheritdoc />
    public long LastModified { get; set; }

    /// <summary>When the file was last modified.</summary>
    public DateTimeOffset LastModifiedTime => DateTimeOffset.FromUnixTimeMilliseconds(LastModified);

    /// <inheritdoc />
    public bool IsDirectory { get; set; }

    /// <inheritdoc />
    public bool IsAbsolute { get; set; }

    /// <inheritdoc />
    public string? Charset { get; set; }

    /// <summary>Opens the content (set by <see cref="IGenericFileOperations{T}.RetrieveFileAsync"/> or a read lock holding the file open).</summary>
    public Func<GenericFile<T>, Stream>? ContentOpener { get; set; }

    /// <inheritdoc />
    public string? Parent
    {
        get
        {
            if (IsAbsolute)
            {
                return Path.GetDirectoryName(AbsoluteFilePath);
            }

            var path = string.IsNullOrEmpty(EndpointPath) ? RelativeFilePath : Path.Combine(EndpointPath, RelativeFilePath);
            return FileUtil.OnlyPath(path);
        }
    }

    /// <summary>The path as Camel's <c>CamelFilePath</c>: absolute, or the starting directory as configured joined with the relative path.</summary>
    public string FilePath =>
        IsAbsolute ? AbsoluteFilePath : string.IsNullOrEmpty(EndpointPath) ? RelativeFilePath : Path.Combine(EndpointPath, RelativeFilePath);

    /// <inheritdoc />
    object? IGenericFile.FileObject => File;

    /// <inheritdoc />
    public Stream OpenRead() =>
        ContentOpener?.Invoke(this) ?? throw new InvalidOperationException($"The content of {this} has not been retrieved");

    /// <summary>A copy of this entry (same file object and content opener).</summary>
    public GenericFile<T> Copy() => (GenericFile<T>)MemberwiseClone();

    /// <summary>
    /// Points this entry to another name (Camel's <c>changeFileName</c>), used when the file is renamed or moved: a relative name is relative
    /// to the starting directory (a leading starting directory, as <c>${file:parent}</c> gives, is removed first), an absolute one is kept.
    /// </summary>
    /// <param name="newName">The new name.</param>
    public void ChangeFileName(string newName)
    {
        var name = FileUtil.Normalize(newName);
        var endpointFull = Path.GetFullPath(string.IsNullOrEmpty(EndpointPath) ? "." : EndpointPath);
        string absolute;
        if (Path.IsPathRooted(name))
        {
            absolute = Path.GetFullPath(name);
            IsAbsolute = true;
        }
        else
        {
            var prefix = FileUtil.StripTrailingSeparator(FileUtil.Normalize(EndpointPath));
            if (prefix.Length > 0 && name.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                name = name[(prefix.Length + 1)..];
            }

            absolute = Path.GetFullPath(Path.Combine(endpointFull, name));
            IsAbsolute = Path.IsPathRooted(EndpointPath);
        }

        AbsoluteFilePath = absolute;
        RelativeFilePath = FileUtil.IsUnder(absolute, endpointFull) ? Path.GetRelativePath(endpointFull, absolute) : absolute;
        FileName = RelativeFilePath;
        FileNameOnly = Path.GetFileName(absolute);
    }

    /// <summary>Sets the file headers on <paramref name="message"/> (Camel's <c>populateHeaders</c>).</summary>
    /// <param name="message">The message.</param>
    public void PopulateHeaders(Message message)
    {
        message.SetHeader(CaravanHeaders.FileLength, FileLength);
        message.SetHeader(CaravanHeaders.FileLastModified, LastModified);
        if (Parent is { } parent)
        {
            message.SetHeader(CaravanHeaders.FileParent, parent);
        }

        message.SetHeader(CaravanHeaders.FilePath, FilePath);
        message.SetHeader(CaravanHeaders.FileNameOnly, FileNameOnly);
        message.SetHeader(CaravanHeaders.FileName, FileName);
        message.SetHeader(CaravanHeaders.FileNameConsumed, FileName);
        message.SetHeader(CaravanHeaders.FileAbsolute, IsAbsolute);
        message.SetHeader(CaravanHeaders.FileAbsolutePath, AbsoluteFilePath);
        message.SetHeader(CaravanHeaders.FileRelativePath, RelativeFilePath);
    }

    /// <summary>Makes this entry the body of <paramref name="exchange"/>, with its headers and the <c>CaravanFileExchangeFile</c> property.</summary>
    /// <param name="exchange">The exchange.</param>
    public void BindToExchange(Exchange exchange)
    {
        exchange.SetProperty(ExchangePropertyKeys.FileExchangeFile, this);
        exchange.Message.Body = this;
        PopulateHeaders(exchange.Message);
    }

    /// <inheritdoc />
    public override string ToString() => $"GenericFile[{FileName}]";
}
