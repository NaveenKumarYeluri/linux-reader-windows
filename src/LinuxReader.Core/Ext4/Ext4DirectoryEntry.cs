namespace LinuxReader.Core.Ext4;

public sealed class Ext4DirectoryEntry
{
    public uint InodeNumber { get; init; }
    public string Name { get; init; } = string.Empty;
    public byte FileType { get; init; }

    public bool IsDirectory => FileType == 2;
    public bool IsRegularFile => FileType == 1;
    public bool IsSymlink => FileType == 7;

    public string FileTypeName => FileType switch
    {
        1 => "Regular File",
        2 => "Directory",
        3 => "Character Device",
        4 => "Block Device",
        5 => "FIFO",
        6 => "Socket",
        7 => "Symbolic Link",
        _ => "Unknown"
    };

    public override string ToString() => $"{Name} (Inode: {InodeNumber}, Type: {FileTypeName})";
}
