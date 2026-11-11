using LinuxReader.Core.IO;

namespace LinuxReader.Core.Partitions;

public static class MbrReader
{
    public static bool TryReadPartitions(Stream stream, int sectorSize, out List<PartitionInfo> partitions)
    {
        partitions = new List<PartitionInfo>();

        if (stream.Length < 512)
        {
            return false;
        }

        stream.Seek(0, SeekOrigin.Begin);
        byte[] sector0 = new byte[512];
        int read = stream.Read(sector0, 0, 512);
        if (read < 512)
        {
            return false;
        }

        // Validate MBR boot signature 0x55, 0xAA
        if (sector0[510] != 0x55 || sector0[511] != 0xAA)
        {
            return false;
        }

        // Check 4 primary partition records at 0x1BE (446), 0x1CE (462), 0x1DE (478), 0x1EE (494)
        for (int i = 0; i < 4; i++)
        {
            int offset = 446 + (i * 16);
            byte type = sector0[offset + 4];

            if (type == 0x00)
            {
                continue; // Empty slot
            }

            if (type == 0xEE)
            {
                // GPT protective MBR - this disk is GPT formatted, not legacy MBR
                return false;
            }

            uint startLba = BitConverter.ToUInt32(sector0, offset + 8);
            uint sectorCount = BitConverter.ToUInt32(sector0, offset + 12);

            if (sectorCount == 0)
            {
                continue;
            }

            long startOffset = (long)startLba * sectorSize;
            long length = (long)sectorCount * sectorSize;

            bool isLinux = (type == 0x83);
            string description = GetMbrTypeDescription(type);

            partitions.Add(new PartitionInfo(
                PartitionIndex: partitions.Count + 1,
                Name: $"Partition {i + 1} ({description})",
                PartitionType: "MBR",
                StartSector: startLba,
                SectorCount: sectorCount,
                StartOffset: startOffset,
                Length: length,
                FormattedSize: DeviceManager.FormatBytes(length),
                IsLinux: isLinux,
                FileSystemType: isLinux ? "Linux (Ext4/Ext3/Ext2)" : description));
        }

        return partitions.Count > 0;
    }

    private static string GetMbrTypeDescription(byte type)
    {
        return type switch
        {
            0x83 => "Linux Native (ext2/3/4)",
            0x82 => "Linux Swap",
            0x85 => "Linux Extended",
            0x07 => "NTFS / exFAT",
            0x0B or 0x0C => "FAT32",
            0x01 => "FAT12",
            0x04 or 0x06 or 0x0E => "FAT16",
            _ => $"Type 0x{type:X2}"
        };
    }
}
