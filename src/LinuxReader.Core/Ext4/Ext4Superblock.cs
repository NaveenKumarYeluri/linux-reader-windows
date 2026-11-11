using System.Text;

namespace LinuxReader.Core.Ext4;

public sealed class Ext4Superblock
{
    public const ushort EXT4_MAGIC = 0xEF53;
    public const int SUPERBLOCK_OFFSET = 1024;
    public const int SUPERBLOCK_SIZE = 1024;

    // INCOMPAT flags
    public const uint INCOMPAT_FILETYPE = 0x0002;
    public const uint INCOMPAT_RECOVER = 0x0004;
    public const uint INCOMPAT_JOURNAL_DEV = 0x0008;
    public const uint INCOMPAT_META_BG = 0x0010;
    public const uint INCOMPAT_EXTENTS = 0x0040;
    public const uint INCOMPAT_64BIT = 0x0080;
    public const uint INCOMPAT_MMP = 0x0100;
    public const uint INCOMPAT_FLEX_BG = 0x0200;
    public const uint INCOMPAT_DIRDATA = 0x1000;
    public const uint INCOMPAT_CSUM_SEED = 0x2000;
    public const uint INCOMPAT_LARGEDIR = 0x4000;
    public const uint INCOMPAT_INLINE_DATA = 0x8000;
    public const uint INCOMPAT_ENCRYPT = 0x10000;

    public uint InodesCount { get; init; }
    public ulong BlocksCount { get; init; }
    public uint FirstDataBlock { get; init; }
    public uint BlockSize { get; init; }
    public uint BlocksPerGroup { get; init; }
    public uint InodesPerGroup { get; init; }
    public ushort Magic { get; init; }
    public ushort State { get; init; }
    public uint FirstInode { get; init; }
    public ushort InodeSize { get; init; }
    public uint FeatureCompat { get; init; }
    public uint FeatureIncompat { get; init; }
    public uint FeatureRoCompat { get; init; }
    public Guid Uuid { get; init; }
    public string VolumeName { get; init; } = string.Empty;
    public string LastMountedPath { get; init; } = string.Empty;
    public ushort DescSize { get; init; }

    public bool HasExtents => (FeatureIncompat & INCOMPAT_EXTENTS) != 0;
    public bool Is64Bit => (FeatureIncompat & INCOMPAT_64BIT) != 0;
    public bool HasFlexBg => (FeatureIncompat & INCOMPAT_FLEX_BG) != 0;
    public bool IsValid => Magic == EXT4_MAGIC;

    public int DescriptorSize => Is64Bit && DescSize >= 64 ? DescSize : 32;

    public static bool TryRead(Stream stream, long partitionOffset, out Ext4Superblock superblock)
    {
        superblock = null!;

        if (stream.Length < partitionOffset + SUPERBLOCK_OFFSET + SUPERBLOCK_SIZE)
        {
            return false;
        }

        byte[] raw = new byte[SUPERBLOCK_SIZE];
        stream.Seek(partitionOffset + SUPERBLOCK_OFFSET, SeekOrigin.Begin);
        int read = stream.Read(raw, 0, SUPERBLOCK_SIZE);
        if (read < SUPERBLOCK_SIZE)
        {
            return false;
        }

        ushort magic = BitConverter.ToUInt16(raw, 56);
        if (magic != EXT4_MAGIC)
        {
            return false;
        }

        uint logBlockSize = BitConverter.ToUInt32(raw, 24);
        uint blockSize = 1024u << (int)logBlockSize;

        uint blocksCountLo = BitConverter.ToUInt32(raw, 4);
        uint featureIncompat = BitConverter.ToUInt32(raw, 96);
        bool is64Bit = (featureIncompat & INCOMPAT_64BIT) != 0;
        uint blocksCountHi = is64Bit ? BitConverter.ToUInt32(raw, 336) : 0;
        ulong totalBlocks = ((ulong)blocksCountHi << 32) | blocksCountLo;

        ushort descSize = BitConverter.ToUInt16(raw, 254);
        if (descSize == 0) descSize = 32;

        string volName = Encoding.ASCII.GetString(raw, 120, 16).TrimEnd('\0').Trim();
        string lastMounted = Encoding.ASCII.GetString(raw, 136, 64).TrimEnd('\0').Trim();

        byte[] uuidBytes = new byte[16];
        Array.Copy(raw, 104, uuidBytes, 0, 16);
        Guid uuid = new(uuidBytes);

        superblock = new Ext4Superblock
        {
            InodesCount = BitConverter.ToUInt32(raw, 0),
            BlocksCount = totalBlocks,
            FirstDataBlock = BitConverter.ToUInt32(raw, 20),
            BlockSize = blockSize,
            BlocksPerGroup = BitConverter.ToUInt32(raw, 32),
            InodesPerGroup = BitConverter.ToUInt32(raw, 40),
            Magic = magic,
            State = BitConverter.ToUInt16(raw, 58),
            FirstInode = BitConverter.ToUInt32(raw, 84),
            InodeSize = BitConverter.ToUInt16(raw, 88),
            FeatureCompat = BitConverter.ToUInt32(raw, 92),
            FeatureIncompat = featureIncompat,
            FeatureRoCompat = BitConverter.ToUInt32(raw, 100),
            Uuid = uuid,
            VolumeName = volName,
            LastMountedPath = lastMounted,
            DescSize = descSize
        };

        return true;
    }
}
