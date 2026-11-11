using System.Text;
using LinuxReader.Core.IO;

namespace LinuxReader.Core.Partitions;

public static class GptReader
{
    private static readonly byte[] GptSignature = Encoding.ASCII.GetBytes("EFI PART");

    // Well-known Partition Type GUIDs
    public static readonly Guid LinuxFilesystemData = new("0FC63DAF-8483-4772-8E79-3D69D8477DE4");
    public static readonly Guid LinuxRootX86_64 = new("4F68BCE3-E8CD-4DB1-96E7-FBCAF984B709");
    public static readonly Guid LinuxRootX86 = new("44479540-F297-412E-BA79-91577746114E");
    public static readonly Guid LinuxRootArm64 = new("69DAD710-2CE4-4E3C-B16C-21A1D49ABED3");
    public static readonly Guid LinuxHome = new("933AC7E1-2EB4-4F13-B844-0E14E2AEF915");
    public static readonly Guid LinuxSrv = new("3B8F8425-20E0-4F3B-907F-1A25A76F98E8");
    public static readonly Guid LinuxSwap = new("0657FD6D-A4AB-43C4-84E5-0933C84B4F4F");
    public static readonly Guid EfiSystemPartition = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    public static readonly Guid MicrosoftBasicData = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");

    public static bool TryReadPartitions(Stream stream, int sectorSize, out List<PartitionInfo> partitions)
    {
        partitions = new List<PartitionInfo>();

        if (stream.Length < sectorSize * 34)
        {
            return false;
        }

        // LBA 1: GPT Header
        stream.Seek(sectorSize, SeekOrigin.Begin);
        byte[] headerBytes = new byte[sectorSize];
        int read = stream.Read(headerBytes, 0, sectorSize);
        if (read < 92)
        {
            return false;
        }

        // Verify "EFI PART" signature
        if (!headerBytes.AsSpan(0, 8).SequenceEqual(GptSignature))
        {
            return false;
        }

        long partitionTableLba = BitConverter.ToInt64(headerBytes, 72);
        int numPartitionEntries = BitConverter.ToInt32(headerBytes, 80);
        int partitionEntrySize = BitConverter.ToInt32(headerBytes, 84);

        if (numPartitionEntries <= 0 || partitionEntrySize < 128)
        {
            return false;
        }

        long tableOffset = partitionTableLba * sectorSize;
        if (tableOffset >= stream.Length)
        {
            return false;
        }

        stream.Seek(tableOffset, SeekOrigin.Begin);
        byte[] entryBytes = new byte[partitionEntrySize];

        for (int i = 0; i < numPartitionEntries; i++)
        {
            int entryRead = stream.Read(entryBytes, 0, partitionEntrySize);
            if (entryRead < partitionEntrySize)
            {
                break;
            }

            Guid typeGuid = new(entryBytes.AsSpan(0, 16));
            if (typeGuid == Guid.Empty)
            {
                continue; // Unused partition entry
            }

            long startLba = BitConverter.ToInt64(entryBytes, 32);
            long endLba = BitConverter.ToInt64(entryBytes, 40);
            long sectorCount = (endLba >= startLba) ? (endLba - startLba + 1) : 0;
            long startOffset = startLba * sectorSize;
            long length = sectorCount * sectorSize;

            string partName = Encoding.Unicode.GetString(entryBytes, 56, 72).TrimEnd('\0').Trim();

            bool isLinux = IsLinuxGuid(typeGuid);
            string typeDescription = GetGuidDescription(typeGuid, partName);

            partitions.Add(new PartitionInfo(
                PartitionIndex: partitions.Count + 1,
                Name: string.IsNullOrWhiteSpace(partName) ? typeDescription : partName,
                PartitionType: "GPT",
                StartSector: startLba,
                SectorCount: sectorCount,
                StartOffset: startOffset,
                Length: length,
                FormattedSize: DeviceManager.FormatBytes(length),
                IsLinux: isLinux,
                FileSystemType: isLinux ? "Linux (Ext4/Btrfs/XFS)" : typeDescription));
        }

        return partitions.Count > 0;
    }

    public static bool IsLinuxGuid(Guid guid)
    {
        return guid == LinuxFilesystemData
            || guid == LinuxRootX86_64
            || guid == LinuxRootX86
            || guid == LinuxRootArm64
            || guid == LinuxHome
            || guid == LinuxSrv;
    }

    private static string GetGuidDescription(Guid guid, string defaultName)
    {
        if (guid == LinuxFilesystemData) return "Linux Filesystem Data";
        if (guid == LinuxRootX86_64) return "Linux Root (x86-64)";
        if (guid == LinuxRootX86) return "Linux Root (x86)";
        if (guid == LinuxRootArm64) return "Linux Root (ARM64)";
        if (guid == LinuxHome) return "Linux /home";
        if (guid == LinuxSrv) return "Linux /srv";
        if (guid == LinuxSwap) return "Linux Swap";
        if (guid == EfiSystemPartition) return "EFI System Partition";
        if (guid == MicrosoftBasicData) return "Microsoft Basic Data";

        return !string.IsNullOrWhiteSpace(defaultName) ? defaultName : guid.ToString();
    }
}
