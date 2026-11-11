using LinuxReader.Core.IO;
using LinuxReader.Core.Partitions;
using LinuxReader.Core;
using Xunit;

namespace LinuxReader.Tests;

public class SafeDiskStreamTests
{
    [Fact]
    public void SafeDiskStream_HandlesUnalignedReadsAndSeeks_Correctly()
    {
        // 256 KB buffer with known byte pattern
        byte[] data = new byte[256 * 1024];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        using var memoryStream = new MemoryStream(data);
        using var safeStream = new SafeDiskStream(memoryStream, data.Length, ownsStream: false);

        Assert.Equal(data.Length, safeStream.Length);
        Assert.False(safeStream.CanWrite);
        Assert.True(safeStream.CanRead);
        Assert.True(safeStream.CanSeek);

        // 1. Read small unaligned slice
        safeStream.Seek(137, SeekOrigin.Begin);
        byte[] buffer = new byte[17];
        int read = safeStream.Read(buffer, 0, buffer.Length);

        Assert.Equal(17, read);
        for (int i = 0; i < 17; i++)
        {
            Assert.Equal((byte)((137 + i) % 251), buffer[i]);
        }

        // 2. Read crossing 64KB cache block boundary
        safeStream.Seek(65530, SeekOrigin.Begin);
        byte[] crossBuffer = new byte[20];
        int crossRead = safeStream.Read(crossBuffer, 0, crossBuffer.Length);

        Assert.Equal(20, crossRead);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal((byte)((65530 + i) % 251), crossBuffer[i]);
        }
    }

    [Fact]
    public void SafeDiskStream_SeekOutOfBounds_ThrowsIOException()
    {
        byte[] data = new byte[1024];
        using var memoryStream = new MemoryStream(data);
        using var safeStream = new SafeDiskStream(memoryStream, data.Length, ownsStream: false);

        Assert.Throws<IOException>(() => safeStream.Seek(2048, SeekOrigin.Begin));
        Assert.Throws<IOException>(() => safeStream.Seek(-1, SeekOrigin.Begin));
    }

    [Fact]
    public void PartitionScanner_DetectsExt4Superblock_OnSyntheticDisk()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"linux-test-{Guid.NewGuid()}.img");
        try
        {
            SampleImageGenerator.GenerateSampleDiskImage(tempPath);
            using var fileStream = SafeDiskStream.OpenFileImage(tempPath);

            var partitions = PartitionScanner.Scan(fileStream);

            Assert.NotEmpty(partitions);
            var linuxPart = partitions.FirstOrDefault(p => p.IsLinux);
            Assert.NotNull(linuxPart);
            Assert.Contains("Ext4", linuxPart.FileSystemType);
            Assert.Contains("LinuxDemoDisk", linuxPart.Name);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
