using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LinuxReader.Core.IO;

/// <summary>
/// Provides safe, strictly read-only access to physical hard drives (\\.\PhysicalDriveN),
/// volume devices, or raw disk image files (.img, .raw, .vhd).
/// Ensures handles are opened with read-only permissions and handles sector buffering.
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

    private readonly Stream _underlyingStream;
    private readonly bool _ownsStream;
    private readonly long _length;
    private bool _disposed;

    public SafeDiskStream(Stream stream, long length = -1, bool ownsStream = true)
    {
        _underlyingStream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = ownsStream;
        _length = length >= 0 ? length : (stream.CanSeek ? stream.Length : 0);
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

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.RandomAccess);
        return new SafeDiskStream(fileStream, fileStream.Length, ownsStream: true);
    }

    /// <summary>
    /// Opens a physical drive (e.g., \\.\PhysicalDrive0) in strictly read-only mode.
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

        var fileStream = new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: false);
        return new SafeDiskStream(fileStream, knownLength, ownsStream: true);
    }

    public override bool CanRead => !_disposed && _underlyingStream.CanRead;
    public override bool CanSeek => !_disposed && _underlyingStream.CanSeek;
    public override bool CanWrite => false; // Strictly read-only

    public override long Length
    {
        get
        {
            EnsureNotDisposed();
            return _length >= 0 ? _length : _underlyingStream.Length;
        }
    }

    public override long Position
    {
        get
        {
            EnsureNotDisposed();
            return _underlyingStream.Position;
        }
        set
        {
            EnsureNotDisposed();
            _underlyingStream.Position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        EnsureNotDisposed();
        return _underlyingStream.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        EnsureNotDisposed();
        return _underlyingStream.Read(buffer);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        EnsureNotDisposed();
        return _underlyingStream.Seek(offset, origin);
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
                _underlyingStream.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
