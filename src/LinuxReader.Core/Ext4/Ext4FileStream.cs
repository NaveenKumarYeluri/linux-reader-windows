namespace LinuxReader.Core.Ext4;

public sealed class Ext4FileStream : Stream
{
    private readonly Stream _partitionStream;
    private readonly uint _blockSize;
    private readonly Ext4Inode _inode;
    private readonly List<Ext4ExtentRange> _extents;
    private long _position;
    private bool _disposed;

    public Ext4FileStream(Stream partitionStream, uint blockSize, Ext4Inode inode, List<Ext4ExtentRange> extents)
    {
        _partitionStream = partitionStream ?? throw new ArgumentNullException(nameof(partitionStream));
        _blockSize = blockSize;
        _inode = inode ?? throw new ArgumentNullException(nameof(inode));
        _extents = extents ?? throw new ArgumentNullException(nameof(extents));
        _position = 0;
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            EnsureNotDisposed();
            return (long)_inode.Size;
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
            if (value < 0 || value > (long)_inode.Size)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Position must be within file length.");
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

        long fileLength = (long)_inode.Size;
        if (_position >= fileLength || count == 0)
        {
            return 0;
        }

        int bytesToRead = (int)Math.Min(count, fileLength - _position);
        int totalRead = 0;

        while (bytesToRead > 0)
        {
            ulong logicalBlock = (ulong)(_position / _blockSize);
            int blockOffset = (int)(_position % _blockSize);
            int inBlockLeft = (int)_blockSize - blockOffset;
            int chunk = Math.Min(bytesToRead, inBlockLeft);

            // Find physical block mapping
            ulong physicalBlock = ResolvePhysicalBlock(logicalBlock);

            if (physicalBlock == 0)
            {
                // Sparse hole in file: fill with zeroes
                Array.Clear(buffer, offset + totalRead, chunk);
            }
            else
            {
                long physicalOffset = (long)(physicalBlock * _blockSize) + blockOffset;
                lock (_partitionStream)
                {
                    _partitionStream.Seek(physicalOffset, SeekOrigin.Begin);
                    int r = _partitionStream.Read(buffer, offset + totalRead, chunk);
                    if (r < chunk)
                    {
                        // End of stream hit unexpectedly
                        Array.Clear(buffer, offset + totalRead + r, chunk - r);
                    }
                }
            }

            _position += chunk;
            totalRead += chunk;
            bytesToRead -= chunk;
        }

        return totalRead;
    }

    private ulong ResolvePhysicalBlock(ulong logicalBlock)
    {
        for (int i = 0; i < _extents.Count; i++)
        {
            var ext = _extents[i];
            if (logicalBlock >= ext.LogicalBlock && logicalBlock < ext.LogicalBlock + ext.BlockCount)
            {
                return ext.PhysicalBlock + (logicalBlock - ext.LogicalBlock);
            }
        }
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        EnsureNotDisposed();
        long newPos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => (long)_inode.Size + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPos < 0 || newPos > (long)_inode.Size)
        {
            throw new IOException($"Seek out of bounds: {newPos}, length: {_inode.Size}");
        }

        _position = newPos;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException("LinuxReader is strictly read-only.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("LinuxReader is strictly read-only.");

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Ext4FileStream));
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
