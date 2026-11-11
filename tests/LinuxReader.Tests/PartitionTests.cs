using System.Text;
using LinuxReader.Core.Partitions;
using Xunit;

namespace LinuxReader.Tests;

public class PartitionTests
{
    [Fact]
    public void MbrReader_ParsesLinuxPartition_Correctly()
    {
        byte[] mbr = new byte[512];
        // Boot signature 0x55, 0xAA
        mbr[510] = 0x55;
        mbr[511] = 0xAA;

        // Partition 1 at 446 (0x1BE)
        int p1 = 446;
        mbr[p1 + 4] = 0x83; // Linux partition type
        BitConverter.GetBytes((uint)2048).CopyTo(mbr, p1 + 8);  // Start LBA: 2048
        BitConverter.GetBytes((uint)100000).CopyTo(mbr, p1 + 12); // Sector count: 100,000

        using var stream = new MemoryStream(mbr);
        bool success = MbrReader.TryReadPartitions(stream, 512, out var partitions);

        Assert.True(success);
        Assert.Single(partitions);
        Assert.Equal(1, partitions[0].PartitionIndex);
        Assert.True(partitions[0].IsLinux);
        Assert.Equal(2048, partitions[0].StartSector);
        Assert.Equal(100000, partitions[0].SectorCount);
        Assert.Equal(2048 * 512, partitions[0].StartOffset);
    }

    [Fact]
    public void GptReader_ParsesLinuxGuidPartition_Correctly()
    {
        int sectorSize = 512;
        byte[] disk = new byte[sectorSize * 34];

        // LBA 1: GPT Header
        int gptHeaderOffset = sectorSize;
        Encoding.ASCII.GetBytes("EFI PART").CopyTo(disk, gptHeaderOffset + 0);
        BitConverter.GetBytes(2L).CopyTo(disk, gptHeaderOffset + 72); // Table LBA = 2
        BitConverter.GetBytes(128).CopyTo(disk, gptHeaderOffset + 80); // 128 entries
        BitConverter.GetBytes(128).CopyTo(disk, gptHeaderOffset + 84); // 128 bytes per entry

        // LBA 2: First partition entry
        int p1Offset = sectorSize * 2;
        GptReader.LinuxFilesystemData.ToByteArray().CopyTo(disk, p1Offset + 0);
        BitConverter.GetBytes(2048L).CopyTo(disk, p1Offset + 32);  // Start LBA
        BitConverter.GetBytes(102047L).CopyTo(disk, p1Offset + 40); // End LBA
        Encoding.Unicode.GetBytes("LinuxRoot").CopyTo(disk, p1Offset + 56);

        using var stream = new MemoryStream(disk);
        bool success = GptReader.TryReadPartitions(stream, sectorSize, out var partitions);

        Assert.True(success);
        Assert.Single(partitions);
        Assert.True(partitions[0].IsLinux);
        Assert.Equal("LinuxRoot", partitions[0].Name);
        Assert.Equal(2048, partitions[0].StartSector);
        Assert.Equal(100000, partitions[0].SectorCount);
    }
}
