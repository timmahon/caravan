using Caravan.Spi;

namespace Caravan.Streams;

/// <summary>
/// An in-memory <see cref="IStreamCache"/> (Camel's <c>ByteArrayInputStreamCache</c>): a read-only, seekable stream over a byte array. Copies
/// share the array (it is never written to) and have their own position. Disposing it does nothing: the data stays readable.
/// </summary>
public sealed class ByteArrayStreamCache : Stream, IStreamCache
{
    private readonly byte[] _buffer;
    private readonly int _length;
    private long _position;

    /// <summary>Creates a cache over the whole of <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The cached data; it must not change afterwards.</param>
    public ByteArrayStreamCache(byte[] buffer)
        : this(buffer, buffer.Length)
    {
    }

    /// <summary>Creates a cache over the first <paramref name="length"/> bytes of <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The cached data; it must not change afterwards.</param>
    /// <param name="length">The number of bytes used.</param>
    public ByteArrayStreamCache(byte[] buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, buffer.Length);
        _buffer = buffer;
        _length = length;
    }

    /// <inheritdoc />
    public bool IsInMemory => true;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc cref="IStreamCache.Length" />
    public override long Length => _length;

    /// <inheritdoc />
    public override long Position
    {
        get => Interlocked.Read(ref _position);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Interlocked.Exchange(ref _position, value);
        }
    }

    /// <summary>The cached bytes (a copy).</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

    /// <inheritdoc />
    public void Reset() => Position = 0;

    /// <inheritdoc />
    public IStreamCache Copy() => new ByteArrayStreamCache(_buffer, _length);

    /// <inheritdoc />
    public async Task WriteToAsync(Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        await output.WriteAsync(_buffer.AsMemory(0, _length), cancellationToken).ConfigureAwait(false);
        Reset();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var position = Position;
        if (position >= _length)
        {
            return 0;
        }

        var n = (int)Math.Min(buffer.Length, _length - position);
        _buffer.AsSpan((int)position, n).CopyTo(buffer);
        Position = position + n;
        return n;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? Task.FromCanceled<int>(cancellationToken) : Task.FromResult(Read(buffer, offset, count));

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<int>(cancellationToken) : ValueTask.FromResult(Read(buffer.Span));

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => _length + offset,
        };
        Position = target;
        return target;
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException("A stream cache is read-only");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("A stream cache is read-only");

    /// <inheritdoc />
    public override string ToString() => $"ByteArrayStreamCache[length={_length}]";

    /// <summary>Does nothing: the data stays readable (it is reclaimed with the object).</summary>
    /// <param name="disposing">Unused.</param>
    protected override void Dispose(bool disposing)
    {
    }
}
