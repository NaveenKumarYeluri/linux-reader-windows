using System.ComponentModel;
using System.Runtime.InteropServices;
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
        IntPtr lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

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

            // Get disk length
            long diskSize = 0;
            if (DeviceIoControl(handle, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, out long length, sizeof(long), out _, IntPtr.Zero))
            {
                diskSize = length;
            }

            drives.Add(new PhysicalDriveInfo(
                Index: i,
                DevicePath: devicePath,
                FriendlyName: $"Physical Drive {i}",
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
