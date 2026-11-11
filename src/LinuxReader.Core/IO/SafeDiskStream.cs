using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LinuxReader.Core.IO;

/// <summary>
/// Provides safe, strictly read-only, sector-buffered access to physical hard drives (\\.\PhysicalDriveN),
/// volume devices, or raw disk image files (.img, .raw, .vhd, .iso, .ext4).
/// Ensures handles are opened with read-only permissions and guarantees sector-aligned I/O
/// so that unaligned reads, small reads (e.g. 8B, 128B, 256B), and seeks never fail with ERROR_INVALID_PARAMETER (87).
/// </summary>
public sealed class SafeDiskStream : Stream
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(
        SafeFileHandle hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    private readonly SafeFileHandle? _deviceHandle;
    private readonly Stream? _underlyingStream;
    private readonly bool _ownsStream;
    private readonly long _length;
    private readonly object _lock = new();

    private long _position;
    private bool _disposed;

    // 64 KB sector cache (64KB is an exact multiple of 512B and 4096B sector sizes)
    private const int SectorSize = 512;
    private const int CacheBlockSize = 64 * 1024;
    private readonly byte[] _cacheBuffer = new byte[CacheBlockSize];
    private long _cacheOffset = -1;
    private int _cacheLength = 0;

    /// <summary>
    /// Creates a SafeDiskStream from an existing Stream (e.g. FileStream or MemoryStream).
    /// </summary>
    public SafeDiskStream(Stream stream, long length = -1, bool ownsStream = true)
    {
        _underlyingStream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = ownsStream;
        _length = length >= 0 ? length : (stream.CanSeek ? stream.Length : 0);
        _position = 0;
    }

    /// <summary>
    /// Creates a SafeDiskStream directly from an elevated Win32 device handle.
    /// </summary>
    public SafeDiskStream(SafeFileHandle deviceHandle, long length, bool ownsHandle = true)
    {
        _deviceHandle = deviceHandle ?? throw new ArgumentNullException(nameof(deviceHandle));
        _ownsStream = ownsHandle;
        _length = length >= 0 ? length : 0;
        _position = 0;
    }

    /// <summary>
    /// Opens a disk image file in strictly read-only mode.
    /// </summary>
    public static SafeDiskStream OpenFileImage(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Disk image file not found: {filePath}", filePath);
        }

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, CacheBlockSize, FileOptions.RandomAccess);
        return new SafeDiskStream(fileStream, fileStream.Length, ownsStream: true);
    }

    /// <summary>
    /// Opens a physical drive (e.g., \\.\PhysicalDrive0) in strictly read-only mode with sector buffering.
    /// Requires administrative privileges in Windows.
    /// </summary>
    public static SafeDiskStream OpenPhysicalDrive(string devicePath, long knownLength = -1)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            throw new ArgumentException("Device path cannot be null or empty.", nameof(devicePath));
        }

        SafeFileHandle handle = CreateFile(
            devicePath,
            GENERIC_READ, // Strictly read-only
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"Failed to open physical device '{devicePath}' in read-only mode. Administrator privileges may be required. Error code: {error}");
        }

        long length = knownLength;
        if (length <= 0)
        {
            length = DeviceManager.GetDriveLength(handle);
        }

        return new SafeDiskStream(handle, length, ownsHandle: true);
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false; // Strictly read-only guarantee

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
                throw new ArgumentOutOfRangeException(nameof(value), $"Position must be within bounds 0..{_length}.");
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

        int totalRead = 0;
        lock (_lock)
        {
            while (totalRead < count && _position < _length)
            {
                // 1. If current position is in cached buffer, copy from it
                if (_position >= _cacheOffset && _position < _cacheOffset + _cacheLength)
                {
                    int inCache = (int)(_position - _cacheOffset);
                    int available = _cacheLength - inCache;
                    int toCopy = Math.Min(available, count - totalRead);

                    Array.Copy(_cacheBuffer, inCache, buffer, offset + totalRead, toCopy);
                    _position += toCopy;
                    totalRead += toCopy;
                    continue;
                }

                // 2. Fetch new cache block aligned to 64KB boundary
                long alignedOffset = (_position / CacheBlockSize) * CacheBlockSize;
                long remaining = _length - alignedOffset;
                if (remaining <= 0)
                {
                    break;
                }

                int toRead = (int)Math.Min(CacheBlockSize, remaining);

                // For physical drives, align read size to sector boundary
                int sectorAlignedToRead = ((toRead + SectorSize - 1) / SectorSize) * SectorSize;
                if (alignedOffset + sectorAlignedToRead > _length && _length > alignedOffset)
                {
                    sectorAlignedToRead = (int)Math.Min(sectorAlignedToRead, _length - alignedOffset);
                }

                int bytesRead = ReadRaw(alignedOffset, _cacheBuffer, 0, sectorAlignedToRead);
                if (bytesRead <= 0)
                {
                    break;
                }

                _cacheOffset = alignedOffset;
                _cacheLength = bytesRead;
            }
        }

        return totalRead;
    }

    public override int Read(Span<byte> buffer)
    {
        byte[] temp = new byte[buffer.Length];
        int read = Read(temp, 0, temp.Length);
        if (read > 0)
        {
            temp.AsSpan(0, read).CopyTo(buffer);
        }
        return read;
    }

    private int ReadRaw(long fileOffset, byte[] targetBuffer, int targetOffset, int count)
    {
        if (_deviceHandle != null)
        {
            // Direct Win32 seek and read on raw physical drive handle
            if (!SetFilePointerEx(_deviceHandle, fileOffset, out _, 0 /* FILE_BEGIN */))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"Failed to seek physical drive to {fileOffset}. Win32 error: {error}");
            }

            byte[] readBuf = (targetOffset == 0 && count == targetBuffer.Length) ? targetBuffer : new byte[count];
            if (!ReadFile(_deviceHandle, readBuf, (uint)count, out uint readCount, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 38 /* ERROR_HANDLE_EOF */) return 0;
                throw new Win32Exception(error, $"Failed to read from physical drive at {fileOffset} ({count} bytes). Win32 error: {error}");
            }

            if (readBuf != targetBuffer && readCount > 0)
            {
                Array.Copy(readBuf, 0, targetBuffer, targetOffset, (int)readCount);
            }

            return (int)readCount;
        }
        else if (_underlyingStream != null)
        {
            _underlyingStream.Seek(fileOffset, SeekOrigin.Begin);
            return _underlyingStream.Read(targetBuffer, targetOffset, count);
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
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPos < 0 || newPos > _length)
        {
            throw new IOException($"Seek out of bounds: Attempted {newPos}, disk length is {_length}.");
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
            throw new ObjectDisposedException(nameof(SafeDiskStream));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing && _ownsStream)
            {
                _deviceHandle?.Dispose();
                _underlyingStream?.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
