using LinuxReader.Core.Ext4;
using LinuxReader.Core.IO;

namespace LinuxReader.Core.Partitions;

public static class PartitionScanner
{
    public static List<PartitionInfo> Scan(Stream stream, int sectorSize = 512)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // 1. Try GPT
        if (GptReader.TryReadPartitions(stream, sectorSize, out var gptPartitions) && gptPartitions.Count > 0)
        {
            return gptPartitions;
        }

        // 2. Try MBR
        if (MbrReader.TryReadPartitions(stream, sectorSize, out var mbrPartitions) && mbrPartitions.Count > 0)
        {
            return mbrPartitions;
        }

        // 3. Fallback: Check if the whole stream is directly a raw Ext2/3/4 filesystem
        if (Ext4Superblock.TryRead(stream, 0, out var superblock) && superblock.IsValid)
        {
            return new List<PartitionInfo>
            {
                new PartitionInfo(
                    PartitionIndex: 1,
                    Name: string.IsNullOrWhiteSpace(superblock.VolumeName) ? "Raw Linux Ext4 Filesystem" : superblock.VolumeName,
                    PartitionType: "Direct Filesystem Image",
                    StartSector: 0,
                    SectorCount: stream.Length / sectorSize,
                    StartOffset: 0,
                    Length: stream.Length,
                    FormattedSize: DeviceManager.FormatBytes(stream.Length),
                    IsLinux: true,
                    FileSystemType: "Ext4")
            };
        }

        return new List<PartitionInfo>();
    }
}
