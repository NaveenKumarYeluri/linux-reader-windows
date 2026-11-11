using LinuxReader.Core.IO;

namespace LinuxReader.Core.Ext4;

public sealed class Ext4Item
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public uint InodeNumber { get; init; }
    public Ext4Inode Inode { get; init; } = null!;
    public bool IsDirectory => Inode.IsDirectory;
    public bool IsRegularFile => Inode.IsRegularFile;
    public bool IsSymlink => Inode.IsSymlink;
    public ulong Size => Inode.Size;
    public string FormattedSize => IsDirectory ? string.Empty : DeviceManager.FormatBytes((long)Size);
    public DateTimeOffset ModifiedTime => Inode.ModificationTime;
    public string Permissions => Inode.PermissionsString;
    public string FileTypeName => Inode.FileTypeName;
    public string? SymlinkTarget { get; set; }

    public override string ToString() => $"{FullPath} ({FileTypeName}, {FormattedSize})";
}
