using Microsoft.Win32.SafeHandles;

namespace Caravan.Components.File;

/// <summary>A read-only stream over a file handle held by a read lock, with its own position; disposing it leaves the handle open.</summary>
internal sealed class LockedFileStream(SafeFileHandle handle) : Stream
{
    private long _position;

    public override bool CanRead => !handle.IsClosed;

    public override bool CanSeek => !handle.IsClosed;

    public override bool CanWrite => false;

    public override long Length => RandomAccess.GetLength(handle);

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = RandomAccess.Read(handle, buffer, _position);
        _position += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await RandomAccess.ReadAsync(handle, buffer, _position, cancellationToken).ConfigureAwait(false);
        _position += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => Length + offset,
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
