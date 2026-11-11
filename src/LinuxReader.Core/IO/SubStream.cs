namespace LinuxReader.Core.IO;

/// <summary>
/// A strictly read-only stream representing a slice/window of a base stream (e.g., a partition inside a disk image or physical drive).
/// Guarantees that no read operations can escape the partition boundaries and no write operations can occur.
/// </summary>
public sealed class SubStream : Stream
{
    private readonly Stream _baseStream;
    private readonly long _baseOffset;
    private readonly long _length;
    private readonly bool _leaveOpen;
    private readonly object _lock = new();
    private long _position;
    private bool _disposed;

    public SubStream(Stream baseStream, long baseOffset, long length, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(baseStream);
        if (baseOffset < 0) throw new ArgumentOutOfRangeException(nameof(baseOffset), "Offset cannot be negative.");
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");

        _baseStream = baseStream;
        _baseOffset = baseOffset;
        _length = length;
        _leaveOpen = leaveOpen;
        _position = 0;
    }

    public override bool CanRead => !_disposed && _baseStream.CanRead;
    public override bool CanSeek => !_disposed && _baseStream.CanSeek;
    public override bool CanWrite => false; // Strict read-only guarantee

    public override long Length
    {
        get
        {
            EnsureNotDisposed();
            return _length;
        }
    }

    public override long Position
    {
        get
        {
            EnsureNotDisposed();
            return _position;
        }
        set
        {
            EnsureNotDisposed();
            if (value < 0 || value > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Position must be within the partition bounds.");
            }
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (_position >= _length || count == 0)
        {
            return 0;
        }

        int toRead = (int)Math.Min(count, _length - _position);

        lock (_lock)
        {
            _baseStream.Seek(_baseOffset + _position, SeekOrigin.Begin);
            int read = _baseStream.Read(buffer, offset, toRead);
            _position += read;
            return read;
        }
    }

    public override int Read(Span<byte> buffer)
    {
        EnsureNotDisposed();
        if (_position >= _length || buffer.IsEmpty)
        {
            return 0;
        }

        int toRead = (int)Math.Min(buffer.Length, _length - _position);
        byte[] temp = new byte[toRead];

        lock (_lock)
        {
            _baseStream.Seek(_baseOffset + _position, SeekOrigin.Begin);
            int read = _baseStream.Read(temp, 0, toRead);
            temp.AsSpan(0, read).CopyTo(buffer);
            _position += read;
            return read;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        EnsureNotDisposed();
        long newPos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPos < 0 || newPos > _length)
        {
            throw new IOException($"Seek out of bounds: Attempted {newPos}, partition length is {_length}.");
        }

        _position = newPos;
        return _position;
    }

    public override void Flush()
    {
        // No-op for read-only stream
    }

    public override void SetLength(long value) => throw new NotSupportedException("LinuxReader is strictly read-only.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("LinuxReader is strictly read-only.");
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException("LinuxReader is strictly read-only.");

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SubStream));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing && !_leaveOpen)
            {
                _baseStream.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
