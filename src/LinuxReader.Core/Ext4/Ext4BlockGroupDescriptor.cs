namespace LinuxReader.Core.Ext4;

public sealed class Ext4BlockGroupDescriptor
{
    public uint GroupIndex { get; init; }
    public ulong InodeTableBlock { get; init; }
    public ulong BlockBitmapBlock { get; init; }
    public ulong InodeBitmapBlock { get; init; }
    public ushort FreeBlocksCount { get; init; }
    public ushort FreeInodesCount { get; init; }
    public ushort UsedDirsCount { get; init; }
    public ushort Flags { get; init; }

    public static Ext4BlockGroupDescriptor Parse(byte[] buffer, int offset, int descSize, uint groupIndex)
    {
        uint blockBitmapLo = BitConverter.ToUInt32(buffer, offset + 0);
        uint inodeBitmapLo = BitConverter.ToUInt32(buffer, offset + 4);
        uint inodeTableLo = BitConverter.ToUInt32(buffer, offset + 8);
        ushort freeBlocksLo = BitConverter.ToUInt16(buffer, offset + 12);
        ushort freeInodesLo = BitConverter.ToUInt16(buffer, offset + 14);
        ushort usedDirsLo = BitConverter.ToUInt16(buffer, offset + 16);
        ushort flags = BitConverter.ToUInt16(buffer, offset + 18);

        uint blockBitmapHi = 0;
        uint inodeBitmapHi = 0;
        uint inodeTableHi = 0;

        if (descSize >= 64)
        {
            blockBitmapHi = BitConverter.ToUInt32(buffer, offset + 32);
            inodeBitmapHi = BitConverter.ToUInt32(buffer, offset + 36);
            inodeTableHi = BitConverter.ToUInt32(buffer, offset + 40);
        }

        ulong inodeTable = ((ulong)inodeTableHi << 32) | inodeTableLo;
        ulong blockBitmap = ((ulong)blockBitmapHi << 32) | blockBitmapLo;
        ulong inodeBitmap = ((ulong)inodeBitmapHi << 32) | inodeBitmapLo;

        return new Ext4BlockGroupDescriptor
        {
            GroupIndex = groupIndex,
            InodeTableBlock = inodeTable,
            BlockBitmapBlock = blockBitmap,
            InodeBitmapBlock = inodeBitmap,
            FreeBlocksCount = freeBlocksLo,
            FreeInodesCount = freeInodesLo,
            UsedDirsCount = usedDirsLo,
            Flags = flags
        };
    }
}
