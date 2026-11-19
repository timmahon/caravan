using Caravan.Spi;

namespace Caravan.Streams;

/// <summary>
/// A write-only stream that buffers in memory and, when <see cref="StreamCachingStrategy.SpoolEnabled"/>, overflows to a temporary file in
/// <see cref="StreamCachingStrategy.SpoolDirectory"/> once more than <see cref="StreamCachingStrategy.SpoolThreshold"/> bytes were written
/// (Camel's <c>CachedOutputStream</c>). <see cref="NewStreamCache"/> then returns the data as an <see cref="IStreamCache"/>; a spooled file is
/// deleted when the exchange given to the constructor completes.
/// </summary>
public sealed class CachedOutputStream : Stream
{
    private readonly StreamCachingStrategy _strategy;
    private readonly Exchange? _exchange;
    private MemoryStream? _memory = new();
    private FileStream? _file;
    private string? _path;
    private long _length;
    private bool _closed;

    /// <summary>Creates the stream.</summary>
    /// <param name="strategy">The strategy giving the threshold and spool directory.</param>
    /// <param name="exchange">
    /// The exchange whose completion deletes a spooled file; when null the caller owns the file (deleted with the spool directory when the
    /// context stops).
    /// </param>
    public CachedOutputStream(StreamCachingStrategy strategy, Exchange? exchange)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _strategy = strategy;
        _exchange = exchange;
    }

    /// <summary>Whether the data was spooled to a temporary file.</summary>
    public bool IsSpooled => _file is not null || (_memory is null && _path is not null);

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => !_closed;

    /// <inheritdoc />
    public override long Length => _length;

    /// <inheritdoc />
    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    /// <summary>Closes the stream for writing and returns the written data as a stream cache.</summary>
    /// <returns>A <see cref="ByteArrayStreamCache"/> or, when spooled, a <see cref="FileStreamCache"/>.</returns>
    public IStreamCache NewStreamCache()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _closed = true;
        if (_file is { } file)
        {
            file.Flush();
            file.Dispose();
            _file = null;
            var spool = new SpoolFile(_path!, _length, _strategy);
            _strategy.OnSpooled(_length);
            _exchange?.AddOnCompletion(spool);
            return new FileStreamCache(spool);
        }

        var memory = _memory!;
        _memory = null;
        _strategy.OnCachedInMemory(_length);
        return memory.TryGetBuffer(out var segment) && segment.Offset == 0
            ? new ByteArrayStreamCache(segment.Array!, segment.Count)
            : new ByteArrayStreamCache(memory.ToArray());
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        SpoolIfNeeded(buffer.Length);
        if (_file is { } file)
        {
            file.Write(buffer);
        }
        else
        {
            _memory!.Write(buffer);
        }

        _length += buffer.Length;
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        SpoolIfNeeded(buffer.Length);
        if (_file is { } file)
        {
            await file.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _memory!.Write(buffer.Span);
        }

        _length += buffer.Length;
    }

    /// <inheritdoc />
    public override void Flush() => _file?.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Discards the data when <see cref="NewStreamCache"/> was not called (deleting any temporary file).</summary>
    /// <param name="disposing">Whether called from <see cref="Stream.Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            _memory = null;
            if (_file is { } file)
            {
                file.Dispose();
                _file = null;
                try
                {
                    File.Delete(_path!);
                }
                catch (IOException)
                {
                    // best effort
                }
            }
        }

        base.Dispose(disposing);
    }

    private void SpoolIfNeeded(int incoming)
    {
        if (_file is not null || !_strategy.SpoolEnabled || _length + incoming <= _strategy.SpoolThreshold)
        {
            return;
        }

        _path = _strategy.CreateSpoolFilePath();
        var file = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, _strategy.BufferSize);
        _memory!.Position = 0;
        _memory.CopyTo(file);
        _memory = null;
        _file = file;
    }
}
