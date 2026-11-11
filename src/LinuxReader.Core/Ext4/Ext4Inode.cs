using System.Text;

namespace LinuxReader.Core.Ext4;

public sealed class Ext4Inode
{
    public const uint ROOT_INODE = 2;

    // File type masks in i_mode
    public const ushort S_IFMT   = 0xF000;
    public const ushort S_IFIFO  = 0x1000;
    public const ushort S_IFCHR  = 0x2000;
    public const ushort S_IFDIR  = 0x4000;
    public const ushort S_IFBLK  = 0x6000;
    public const ushort S_IFREG  = 0x8000;
    public const ushort S_IFLNK  = 0xA000;
    public const ushort S_IFSOCK = 0xC000;

    // Inode flags
    public const uint EXT4_EXTENTS_FL = 0x00080000;
    public const uint EXT4_INLINE_DATA_FL = 0x10000000;

    public uint InodeNumber { get; init; }
    public ushort Mode { get; init; }
    public ulong Size { get; init; }
    public DateTimeOffset AccessTime { get; init; }
    public DateTimeOffset ChangeTime { get; init; }
    public DateTimeOffset ModificationTime { get; init; }
    public ushort LinksCount { get; init; }
    public uint Flags { get; init; }
    public byte[] BlockData { get; init; } = Array.Empty<byte>();

    public bool IsDirectory => (Mode & S_IFMT) == S_IFDIR;
    public bool IsRegularFile => (Mode & S_IFMT) == S_IFREG;
    public bool IsSymlink => (Mode & S_IFMT) == S_IFLNK;
    public bool UsesExtents => (Flags & EXT4_EXTENTS_FL) != 0;
    public bool HasInlineData => (Flags & EXT4_INLINE_DATA_FL) != 0;

    public string PermissionsString => FormatPermissions(Mode);

    public string FileTypeName => (Mode & S_IFMT) switch
    {
        S_IFDIR => "Directory",
        S_IFREG => "Regular File",
        S_IFLNK => "Symbolic Link",
        S_IFCHR => "Character Device",
        S_IFBLK => "Block Device",
        S_IFIFO => "FIFO",
        S_IFSOCK => "Socket",
        _ => "Unknown"
    };

    public static Ext4Inode Parse(byte[] buffer, int offset, ushort inodeSize, uint inodeNumber)
    {
        ushort mode = BitConverter.ToUInt16(buffer, offset + 0);
        uint sizeLo = BitConverter.ToUInt32(buffer, offset + 4);
        uint atime = BitConverter.ToUInt32(buffer, offset + 8);
        uint ctime = BitConverter.ToUInt32(buffer, offset + 12);
        uint mtime = BitConverter.ToUInt32(buffer, offset + 16);
        ushort linksCount = BitConverter.ToUInt16(buffer, offset + 26);
        uint flags = BitConverter.ToUInt32(buffer, offset + 32);

        byte[] blockData = new byte[60];
        Array.Copy(buffer, offset + 40, blockData, 0, 60);

        uint sizeHi = 0;
        // Upper 32-bits of size (for regular files and directories in modern ext4)
        if (inodeSize > 128 && ((mode & S_IFMT) == S_IFREG || (mode & S_IFMT) == S_IFDIR))
        {
            sizeHi = BitConverter.ToUInt32(buffer, offset + 108);
        }

        ulong size = ((ulong)sizeHi << 32) | sizeLo;

        return new Ext4Inode
        {
            InodeNumber = inodeNumber,
            Mode = mode,
            Size = size,
            AccessTime = DateTimeOffset.FromUnixTimeSeconds(atime),
            ChangeTime = DateTimeOffset.FromUnixTimeSeconds(ctime),
            ModificationTime = DateTimeOffset.FromUnixTimeSeconds(mtime),
            LinksCount = linksCount,
            Flags = flags,
            BlockData = blockData
        };
    }

    private static string FormatPermissions(ushort mode)
    {
        char typeChar = (mode & S_IFMT) switch
        {
            S_IFDIR => 'd',
            S_IFLNK => 'l',
            S_IFCHR => 'c',
            S_IFBLK => 'b',
            S_IFIFO => 'p',
            S_IFSOCK => 's',
            _ => '-'
        };

        return new string(new[]
        {
            typeChar,
            (mode & 0x0100) != 0 ? 'r' : '-',
            (mode & 0x0080) != 0 ? 'w' : '-',
            (mode & 0x0040) != 0 ? 'x' : '-',
            (mode & 0x0020) != 0 ? 'r' : '-',
            (mode & 0x0010) != 0 ? 'w' : '-',
            (mode & 0x0008) != 0 ? 'x' : '-',
            (mode & 0x0004) != 0 ? 'r' : '-',
            (mode & 0x0002) != 0 ? 'w' : '-',
            (mode & 0x0001) != 0 ? 'x' : '-'
        });
    }
}
