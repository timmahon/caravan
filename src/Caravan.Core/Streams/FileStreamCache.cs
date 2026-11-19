using Caravan.Spi;

namespace Caravan.Streams;

/// <summary>
/// An <see cref="IStreamCache"/> whose data was spooled to a temporary file (Camel's <c>FileInputStreamCache</c>), because it exceeded
/// <see cref="StreamCachingStrategy.SpoolThreshold"/>. The file is deleted when the exchange that created the cache completes; readers
/// (copies included) opened before keep working. Disposing a reader closes its file handle; it is opened again when the reader is read or
/// reset while the file still exists.
/// </summary>
public sealed class FileStreamCache : Stream, IStreamCache
{
    private readonly SpoolFile _file;
    private FileStream? _stream;
    private long _position;

    internal FileStreamCache(SpoolFile file)
    {
        _file = file;
        _stream = file.OpenRead();
    }

    /// <summary>The path of the temporary file.</summary>
    public string FilePath => _file.Path;

    /// <summary>Whether the temporary file was deleted (its exchange completed).</summary>
    public bool IsFileDeleted => _file.IsDeleted;

    /// <inheritdoc />
    public bool IsInMemory => false;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc cref="IStreamCache.Length" />
    public override long Length => _file.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _stream?.Position ?? _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_stream is null && value == _position)
            {
                return;
            }

            Open().Position = value;
        }
    }

    /// <inheritdoc />
    public void Reset() => Position = 0;

    /// <inheritdoc />
    public IStreamCache Copy() => new FileStreamCache(_file);

    /// <inheritdoc />
    public async Task WriteToAsync(Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var stream = Open();
        stream.Position = 0;
        await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        Reset();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Open().Read(buffer, offset, count);

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => Open().Read(buffer);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Open().ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Open().ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => Open().Seek(offset, origin);

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException("A stream cache is read-only");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("A stream cache is read-only");

    /// <inheritdoc />
    public override string ToString() => $"FileStreamCache[{_file.Path}, length={_file.Length}]";

    /// <summary>Closes the file handle of this reader; it is opened again if the reader is used afterwards.</summary>
    /// <param name="disposing">Whether called from <see cref="Stream.Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _stream is { } stream)
        {
            _position = stream.Position;
            _stream = null;
            stream.Dispose();
        }
    }

    private FileStream Open()
    {
        if (_stream is null)
        {
            var stream = _file.OpenRead();
            stream.Position = _position;
            _stream = stream;
        }

        return _stream;
    }
}
