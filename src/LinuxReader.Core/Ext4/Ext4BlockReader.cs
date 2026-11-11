namespace LinuxReader.Core.Ext4;

public sealed class Ext4BlockReader
{
    private const ushort EXT4_EXT_MAGIC = 0xF30A;
    private readonly Stream _stream;
    private readonly uint _blockSize;

    public Ext4BlockReader(Stream stream, uint blockSize)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _blockSize = blockSize;
    }

    /// <summary>
    /// Collects all allocated extent ranges for the given inode.
    /// Supports both Ext4 extents and Ext2/Ext3 direct/indirect block pointers.
    /// </summary>
    public List<Ext4ExtentRange> GetExtents(Ext4Inode inode)
    {
        ArgumentNullException.ThrowIfNull(inode);

        var extents = new List<Ext4ExtentRange>();

        if (inode.UsesExtents)
        {
            ParseExtentNode(inode.BlockData, 0, extents);
        }
        else
        {
            ParseLegacyBlocks(inode, extents);
        }

        // Sort by logical block number
        extents.Sort((a, b) => a.LogicalBlock.CompareTo(b.LogicalBlock));
        return extents;
    }

    private void ParseExtentNode(byte[] data, int offset, List<Ext4ExtentRange> extents)
    {
        if (data.Length < offset + 12)
        {
            return;
        }

        ushort magic = BitConverter.ToUInt16(data, offset + 0);
        if (magic != EXT4_EXT_MAGIC)
        {
            return;
        }

        ushort entries = BitConverter.ToUInt16(data, offset + 2);
        ushort depth = BitConverter.ToUInt16(data, offset + 6);

        int currentOffset = offset + 12;

        if (depth == 0)
        {
            // Leaf extents
            for (int i = 0; i < entries; i++)
            {
                if (data.Length < currentOffset + 12) break;

                uint block = BitConverter.ToUInt32(data, currentOffset + 0);
                ushort len = BitConverter.ToUInt16(data, currentOffset + 4);
                ushort startHi = BitConverter.ToUInt16(data, currentOffset + 6);
                uint startLo = BitConverter.ToUInt32(data, currentOffset + 8);

                // Handle uninitialized extent flag
                uint actualLen = len > 32768 ? (uint)(len - 32768) : len;
                ulong physicalBlock = ((ulong)startHi << 32) | startLo;

                if (actualLen > 0)
                {
                    extents.Add(new Ext4ExtentRange(block, physicalBlock, actualLen));
                }

                currentOffset += 12;
            }
        }
        else
        {
            // Index nodes
            for (int i = 0; i < entries; i++)
            {
                if (data.Length < currentOffset + 12) break;

                uint leafLo = BitConverter.ToUInt32(data, currentOffset + 4);
                ushort leafHi = BitConverter.ToUInt16(data, currentOffset + 8);
                ulong childPhysicalBlock = ((ulong)leafHi << 32) | leafLo;

                // Read child extent block from disk
                byte[] childBlockData = ReadBlock(childPhysicalBlock);
                ParseExtentNode(childBlockData, 0, extents);

                currentOffset += 12;
            }
        }
    }

    private void ParseLegacyBlocks(Ext4Inode inode, List<Ext4ExtentRange> extents)
    {
        byte[] blockData = inode.BlockData;
        ulong logicalBlock = 0;

        // Direct blocks: 0..11
        for (int i = 0; i < 12 && i * 4 < blockData.Length; i++)
        {
            uint physBlock = BitConverter.ToUInt32(blockData, i * 4);
            if (physBlock != 0)
            {
                extents.Add(new Ext4ExtentRange(logicalBlock, physBlock, 1));
            }
            logicalBlock++;
        }

        // Indirect block: 12
        if (blockData.Length >= 52)
        {
            uint indirectBlock = BitConverter.ToUInt32(blockData, 48);
            if (indirectBlock != 0)
            {
                byte[] block = ReadBlock(indirectBlock);
                int count = block.Length / 4;
                for (int i = 0; i < count; i++)
                {
                    uint phys = BitConverter.ToUInt32(block, i * 4);
                    if (phys != 0)
                    {
                        extents.Add(new Ext4ExtentRange(logicalBlock, phys, 1));
                    }
                    logicalBlock++;
                }
            }
        }
    }

    public byte[] ReadBlock(ulong blockNumber)
    {
        byte[] buffer = new byte[_blockSize];
        long offset = (long)(blockNumber * _blockSize);

        lock (_stream)
        {
            _stream.Seek(offset, SeekOrigin.Begin);
            int read = _stream.Read(buffer, 0, (int)_blockSize);
            if (read < (int)_blockSize)
            {
                Array.Clear(buffer, read, (int)_blockSize - read);
            }
        }

        return buffer;
    }
}
