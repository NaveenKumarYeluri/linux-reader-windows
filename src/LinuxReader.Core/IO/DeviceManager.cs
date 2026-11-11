using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LinuxReader.Core.IO;

public static class DeviceManager
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private const uint IOCTL_DISK_GET_LENGTH_INFO = 0x00074059;
    private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

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
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        out long lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[]? lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    /// <summary>
    /// Checks whether the current process is running with elevated Administrator rights.
    /// </summary>
    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Queries the exact total size in bytes of a physical drive handle via Win32 IOCTLs.
    /// </summary>
    public static long GetDriveLength(SafeFileHandle handle)
    {
        if (handle.IsInvalid || handle.IsClosed) return 0;

        // 1. Try IOCTL_DISK_GET_LENGTH_INFO
        if (DeviceIoControl(handle, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, out long length, sizeof(long), out _, IntPtr.Zero))
        {
            if (length > 0) return length;
        }

        // 2. Try IOCTL_DISK_GET_DRIVE_GEOMETRY_EX
        byte[] geomBuffer = new byte[256];
        if (DeviceIoControl(handle, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, 0, geomBuffer, (uint)geomBuffer.Length, out uint bytesReturned, IntPtr.Zero))
        {
            if (bytesReturned >= 32)
            {
                // DISK_GEOMETRY is 24 bytes, followed by DiskSize (8 bytes)
                long diskSize = BitConverter.ToInt64(geomBuffer, 24);
                if (diskSize > 0) return diskSize;
            }
        }

        return 0;
    }

    /// <summary>
    /// Attempts to query the friendly model name of a storage device via IOCTL_STORAGE_QUERY_PROPERTY.
    /// </summary>
    public static string? GetDriveModel(SafeFileHandle handle)
    {
        if (handle.IsInvalid || handle.IsClosed) return null;

        try
        {
            // STORAGE_PROPERTY_QUERY: PropertyId = 0 (StorageDeviceProperty), QueryType = 0 (PropertyStandardQuery)
            byte[] query = new byte[12];
            byte[] outBuf = new byte[1024];

            if (DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, query, (uint)query.Length, outBuf, (uint)outBuf.Length, out uint bytesReturned, IntPtr.Zero))
            {
                if (bytesReturned >= 24)
                {
                    int vendorOffset = BitConverter.ToInt32(outBuf, 12);
                    int productOffset = BitConverter.ToInt32(outBuf, 16);

                    string vendor = "";
                    if (vendorOffset > 0 && vendorOffset < bytesReturned)
                    {
                        int end = vendorOffset;
                        while (end < bytesReturned && outBuf[end] != 0) end++;
                        vendor = Encoding.ASCII.GetString(outBuf, vendorOffset, end - vendorOffset).Trim();
                    }

                    string product = "";
                    if (productOffset > 0 && productOffset < bytesReturned)
                    {
                        int end = productOffset;
                        while (end < bytesReturned && outBuf[end] != 0) end++;
                        product = Encoding.ASCII.GetString(outBuf, productOffset, end - productOffset).Trim();
                    }

                    string fullName = $"{vendor} {product}".Trim();
                    return string.IsNullOrWhiteSpace(fullName) ? null : fullName;
                }
            }
        }
        catch
        {
            // Ignore non-critical query failures
        }

        return null;
    }

    /// <summary>
    /// Scans physical drives on the Windows machine (\\.\PhysicalDrive0 .. PhysicalDrive31)
    /// </summary>
    public static List<PhysicalDriveInfo> GetPhysicalDrives(int maxDrivesToProbe = 16)
    {
        var drives = new List<PhysicalDriveInfo>();

        for (int i = 0; i < maxDrivesToProbe; i++)
        {
            string devicePath = $@"\\.\PhysicalDrive{i}";
            using SafeFileHandle handle = CreateFile(
                devicePath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                // 2 = ERROR_FILE_NOT_FOUND (drive index does not exist)
                if (error == 2)
                {
                    continue;
                }

                // 5 = ERROR_ACCESS_DENIED (drive exists, but requires elevated admin rights)
                if (error == 5)
                {
                    drives.Add(new PhysicalDriveInfo(
                        Index: i,
                        DevicePath: devicePath,
                        FriendlyName: $"Physical Drive {i} (Access Denied - Run as Administrator)",
                        SizeBytes: 0,
                        FormattedSize: "Unknown",
                        IsAccessible: false,
                        StatusMessage: "Administrator privileges required to read raw disk."));
                    continue;
                }

                continue;
            }

            long diskSize = GetDriveLength(handle);
            string? model = GetDriveModel(handle);
            string friendlyName = !string.IsNullOrWhiteSpace(model)
                ? $"Physical Drive {i} ({model})"
                : $"Physical Drive {i}";

            drives.Add(new PhysicalDriveInfo(
                Index: i,
                DevicePath: devicePath,
                FriendlyName: friendlyName,
                SizeBytes: diskSize,
                FormattedSize: FormatBytes(diskSize),
                IsAccessible: true));
        }

        return drives;
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
            if (counter >= suffixes.Length - 1) break;
        }
        return $"{number:0.##} {suffixes[counter]}";
    }
}
