using LinuxReader.Core.Ext4;
using LinuxReader.Core.IO;

namespace LinuxReader.Core.Partitions;

public static class PartitionScanner
{
    public static List<PartitionInfo> Scan(Stream stream, int sectorSize = 512)
    {
        ArgumentNullException.ThrowIfNull(stream);

        List<PartitionInfo>? partitions = null;

        // 1. Try GPT
        if (GptReader.TryReadPartitions(stream, sectorSize, out var gptPartitions) && gptPartitions.Count > 0)
        {
            partitions = gptPartitions;
        }
        // 2. Try MBR
        else if (MbrReader.TryReadPartitions(stream, sectorSize, out var mbrPartitions) && mbrPartitions.Count > 0)
        {
            partitions = mbrPartitions;
        }

        if (partitions != null && partitions.Count > 0)
        {
            // Probe each partition for Ext4 filesystem superblock to detect Linux volumes
            // even when partition type GUID is generic or unknown
            var enriched = new List<PartitionInfo>(partitions.Count);
            foreach (var p in partitions)
            {
                try
                {
                    if (Ext4Superblock.TryRead(stream, p.StartOffset, out var sb) && sb.IsValid)
                    {
                        string vol = string.IsNullOrWhiteSpace(sb.VolumeName) ? "" : sb.VolumeName;
                        string displayName = string.IsNullOrEmpty(vol) ? p.Name : $"{p.Name} (\"{vol}\")";
                        enriched.Add(p with
                        {
                            Name = displayName,
                            IsLinux = true,
                            FileSystemType = "Ext4"
                        });
                        continue;
                    }
                }
                catch
                {
                    // Ignore probe read error and keep original partition info
                }

                enriched.Add(p);
            }
            return enriched;
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
                    SectorCount: sectorSize > 0 ? stream.Length / sectorSize : 0,
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
