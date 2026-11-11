using System.Text;
using LinuxReader.Core.Ext4;
using LinuxReader.Core.Partitions;

namespace LinuxReader.Core;

public static class SampleImageGenerator
{
    public static void GenerateSampleDiskImage(string outputPath)
    {
        uint blockSize = 1024;
        uint totalBlocks = 256;
        int sectorSize = 512;
        int partitionStartLba = 2048; // 1 MB offset (standard alignment)
        int partitionSectorCount = (int)(totalBlocks * blockSize / (uint)sectorSize);
        int totalDiskSectors = partitionStartLba + partitionSectorCount + 34;

        byte[] diskBytes = new byte[totalDiskSectors * sectorSize];

        // 1. Write Protective MBR at LBA 0
        diskBytes[510] = 0x55;
        diskBytes[511] = 0xAA;
        int mbrP1 = 446;
        diskBytes[mbrP1 + 4] = 0xEE; // GPT Protective
        BitConverter.GetBytes(1u).CopyTo(diskBytes, mbrP1 + 8);
        BitConverter.GetBytes((uint)(totalDiskSectors - 1)).CopyTo(diskBytes, mbrP1 + 12);

        // 2. Write GPT Header at LBA 1
        int gptHeaderOffset = sectorSize;
        Encoding.ASCII.GetBytes("EFI PART").CopyTo(diskBytes, gptHeaderOffset + 0);
        BitConverter.GetBytes(0x00010000u).CopyTo(diskBytes, gptHeaderOffset + 8); // Revision 1.0
        BitConverter.GetBytes(92u).CopyTo(diskBytes, gptHeaderOffset + 12); // Header size
        BitConverter.GetBytes(1L).CopyTo(diskBytes, gptHeaderOffset + 24);  // Current LBA 1
        BitConverter.GetBytes(2L).CopyTo(diskBytes, gptHeaderOffset + 72);  // Partition table LBA 2
        BitConverter.GetBytes(128u).CopyTo(diskBytes, gptHeaderOffset + 80); // 128 entries
        BitConverter.GetBytes(128u).CopyTo(diskBytes, gptHeaderOffset + 84); // 128 bytes per entry

        // 3. Write Partition 1 at LBA 2 (Linux Filesystem Data GUID)
        int p1Offset = sectorSize * 2;
        GptReader.LinuxFilesystemData.ToByteArray().CopyTo(diskBytes, p1Offset + 0);
        Guid.NewGuid().ToByteArray().CopyTo(diskBytes, p1Offset + 16);
        BitConverter.GetBytes((long)partitionStartLba).CopyTo(diskBytes, p1Offset + 32);
        BitConverter.GetBytes((long)(partitionStartLba + partitionSectorCount - 1)).CopyTo(diskBytes, p1Offset + 40);
        Encoding.Unicode.GetBytes("Linux Data").CopyTo(diskBytes, p1Offset + 56);

        // 4. Ext4 Filesystem inside Partition
        int partOffset = partitionStartLba * sectorSize;

        // Superblock at block 1 (byte 1024 inside partition)
        int sbOffset = partOffset + (int)blockSize;
        BitConverter.GetBytes(Ext4Superblock.EXT4_MAGIC).CopyTo(diskBytes, sbOffset + 56);
        BitConverter.GetBytes(64u).CopyTo(diskBytes, sbOffset + 0);  // inodes count
        BitConverter.GetBytes(totalBlocks).CopyTo(diskBytes, sbOffset + 4); // blocks count
        BitConverter.GetBytes(1u).CopyTo(diskBytes, sbOffset + 20); // first data block = 1
        BitConverter.GetBytes(0u).CopyTo(diskBytes, sbOffset + 24); // log block size = 0 (1024 bytes)
        BitConverter.GetBytes(totalBlocks).CopyTo(diskBytes, sbOffset + 32); // blocks per group
        BitConverter.GetBytes(64u).CopyTo(diskBytes, sbOffset + 40); // inodes per group
        BitConverter.GetBytes((ushort)128).CopyTo(diskBytes, sbOffset + 88); // inode size
        BitConverter.GetBytes(Ext4Superblock.INCOMPAT_EXTENTS | Ext4Superblock.INCOMPAT_FILETYPE).CopyTo(diskBytes, sbOffset + 96);
        Encoding.ASCII.GetBytes("LinuxDemoDisk").CopyTo(diskBytes, sbOffset + 120);

        // Block Group Descriptor at block 2
        int bgOffset = partOffset + 2 * (int)blockSize;
        BitConverter.GetBytes(5u).CopyTo(diskBytes, bgOffset + 8); // Inode table at block 5

        int inodeTableOffset = partOffset + 5 * (int)blockSize;

        // Inode #2: Root directory (points to block 10)
        int rootInodeOffset = inodeTableOffset + 1 * 128;
        BitConverter.GetBytes((ushort)(0x4000 | 0x01ED)).CopyTo(diskBytes, rootInodeOffset + 0); // Directory 0755
        BitConverter.GetBytes(blockSize).CopyTo(diskBytes, rootInodeOffset + 4);
        BitConverter.GetBytes(Ext4Inode.EXT4_EXTENTS_FL).CopyTo(diskBytes, rootInodeOffset + 32);

        int rootExtOffset = rootInodeOffset + 40;
        BitConverter.GetBytes((ushort)0xF30A).CopyTo(diskBytes, rootExtOffset + 0);
        BitConverter.GetBytes((ushort)1).CopyTo(diskBytes, rootExtOffset + 2);
        BitConverter.GetBytes((ushort)4).CopyTo(diskBytes, rootExtOffset + 4);
        BitConverter.GetBytes(0u).CopyTo(diskBytes, rootExtOffset + 12);
        BitConverter.GetBytes((ushort)1).CopyTo(diskBytes, rootExtOffset + 16);
        BitConverter.GetBytes(10u).CopyTo(diskBytes, rootExtOffset + 20); // Data block 10

        // Inode #12: /welcome.txt
        string file1 = "Welcome to Linux Reader on Windows!\nThis file demonstrates safe, direct read-only access to Linux files.\nZero kernel drivers. Zero corruption risk.";
        byte[] file1Bytes = Encoding.UTF8.GetBytes(file1);

        int file1InodeOffset = inodeTableOffset + 11 * 128;
        BitConverter.GetBytes((ushort)(0x8000 | 0x01A4)).CopyTo(diskBytes, file1InodeOffset + 0); // File 0644
        BitConverter.GetBytes((uint)file1Bytes.Length).CopyTo(diskBytes, file1InodeOffset + 4);
        BitConverter.GetBytes(Ext4Inode.EXT4_EXTENTS_FL).CopyTo(diskBytes, file1InodeOffset + 32);

        int file1ExtOffset = file1InodeOffset + 40;
        BitConverter.GetBytes((ushort)0xF30A).CopyTo(diskBytes, file1ExtOffset + 0);
        BitConverter.GetBytes((ushort)1).CopyTo(diskBytes, file1ExtOffset + 2);
        BitConverter.GetBytes((ushort)4).CopyTo(diskBytes, file1ExtOffset + 4);
        BitConverter.GetBytes(0u).CopyTo(diskBytes, file1ExtOffset + 12);
        BitConverter.GetBytes((ushort)1).CopyTo(diskBytes, file1ExtOffset + 16);
        BitConverter.GetBytes(11u).CopyTo(diskBytes, file1ExtOffset + 20); // Data block 11

        // Inode #13: /etc directory (points to block 12)
        int etcInodeOffset = inodeTableOffset + 12 * 128;
        BitConverter.GetBytes((ushort)(0x4000 | 0x01ED)).CopyTo(diskBytes, etcInodeOffset + 0); // Directory 0755
        BitConverter.GetBytes(blockSize).CopyTo(diskBytes, etcInodeOffset + 4);
        BitConverter.GetBytes(Ext4Inode.EXT4_EXTENTS_FL).CopyTo(diskBytes, etcInodeOffset + 32);

        int etcExtOffset = etcInodeOffset + 40;
        BitConverter.GetBytes((ushort)0xF30A).CopyTo(diskBytes, etcExtOffset + 0);
        BitConverter.GetBytes((ushort)1).CopyTo(diskBytes, etcExtOffset + 2);
        BitConverter.GetBytes((ushort)4).CopyTo(diskBytes, etcExtOffset + 4);
        BitConverter.GetBytes(0u).CopyTo(diskBytes, etcExtOffset + 12);
        BitConverter.GetBytes((ushort)1).CopyTo(diskBytes, etcExtOffset + 16);
        BitConverter.GetBytes(12u).CopyTo(diskBytes, etcExtOffset + 20); // Data block 12

        // Inode #14: /etc/config.json
        string file2 = "{\n  \"appName\": \"LinuxReaderWin\",\n  \"readOnly\": true,\n  \"safetyVerified\": true\n}";
        byte[] file2Bytes = Encoding.UTF8.GetBytes(file2);

        int file2InodeOffset = inodeTableOffset + 13 * 128;
        BitConverter.GetBytes((ushort)(0x8000 | 0x01A4)).CopyTo(diskBytes, file2InodeOffset + 0);
        BitConverter.GetBytes((uint)file2Bytes.Length).CopyTo(diskBytes, file2InodeOffset + 4);
        BitConverter.GetBytes(Ext4Inode.EXT4_EXTENTS_FL).CopyTo(diskBytes, file2InodeOffset + 32);

        int file2ExtOffset = file2InodeOffset + 40;
        BitConverter.GetBytes((ushort)0xF30A).CopyTo(diskBytes, file2ExtOffset + 0);
        BitConverter.GetBytes((ushort)1).CopyTo(diskBytes, file2ExtOffset + 2);
        BitConverter.GetBytes((ushort)4).CopyTo(diskBytes, file2ExtOffset + 4);
        BitConverter.GetBytes(0u).CopyTo(diskBytes, file2ExtOffset + 12);
        BitConverter.GetBytes((ushort)1).CopyTo(diskBytes, file2ExtOffset + 16);
        BitConverter.GetBytes(13u).CopyTo(diskBytes, file2ExtOffset + 20); // Data block 13

        // Block 10: Root Directory Data
        int rootDataOffset = partOffset + 10 * (int)blockSize;
        // Entry 1: "."
        BitConverter.GetBytes(2u).CopyTo(diskBytes, rootDataOffset + 0);
        BitConverter.GetBytes((ushort)12).CopyTo(diskBytes, rootDataOffset + 4);
        diskBytes[rootDataOffset + 6] = 1;
        diskBytes[rootDataOffset + 7] = 2; // dir
        diskBytes[rootDataOffset + 8] = (byte)'.';

        // Entry 2: ".."
        int e2 = rootDataOffset + 12;
        BitConverter.GetBytes(2u).CopyTo(diskBytes, e2 + 0);
        BitConverter.GetBytes((ushort)12).CopyTo(diskBytes, e2 + 4);
        diskBytes[e2 + 6] = 2;
        diskBytes[e2 + 7] = 2; // dir
        diskBytes[e2 + 8] = (byte)'.';
        diskBytes[e2 + 9] = (byte)'.';

        // Entry 3: "welcome.txt"
        int e3 = rootDataOffset + 24;
        BitConverter.GetBytes(12u).CopyTo(diskBytes, e3 + 0);
        BitConverter.GetBytes((ushort)24).CopyTo(diskBytes, e3 + 4);
        diskBytes[e3 + 6] = 11;
        diskBytes[e3 + 7] = 1; // regular file
        Encoding.UTF8.GetBytes("welcome.txt").CopyTo(diskBytes, e3 + 8);

        // Entry 4: "etc"
        int e4 = rootDataOffset + 48;
        BitConverter.GetBytes(13u).CopyTo(diskBytes, e4 + 0);
        BitConverter.GetBytes((ushort)(blockSize - 48)).CopyTo(diskBytes, e4 + 4);
        diskBytes[e4 + 6] = 3;
        diskBytes[e4 + 7] = 2; // dir
        Encoding.UTF8.GetBytes("etc").CopyTo(diskBytes, e4 + 8);

        // Block 11: /welcome.txt Content
        int file1DataOffset = partOffset + 11 * (int)blockSize;
        file1Bytes.CopyTo(diskBytes, file1DataOffset);

        // Block 12: /etc Directory Data
        int etcDataOffset = partOffset + 12 * (int)blockSize;
        // Entry 1: "."
        BitConverter.GetBytes(13u).CopyTo(diskBytes, etcDataOffset + 0);
        BitConverter.GetBytes((ushort)12).CopyTo(diskBytes, etcDataOffset + 4);
        diskBytes[etcDataOffset + 6] = 1;
        diskBytes[etcDataOffset + 7] = 2;
        diskBytes[etcDataOffset + 8] = (byte)'.';

        // Entry 2: ".."
        int etcE2 = etcDataOffset + 12;
        BitConverter.GetBytes(2u).CopyTo(diskBytes, etcE2 + 0);
        BitConverter.GetBytes((ushort)12).CopyTo(diskBytes, etcE2 + 4);
        diskBytes[etcE2 + 6] = 2;
        diskBytes[etcE2 + 7] = 2;
        diskBytes[etcE2 + 8] = (byte)'.';
        diskBytes[etcE2 + 9] = (byte)'.';

        // Entry 3: "config.json"
        int etcE3 = etcDataOffset + 24;
        BitConverter.GetBytes(14u).CopyTo(diskBytes, etcE3 + 0);
        BitConverter.GetBytes((ushort)(blockSize - 24)).CopyTo(diskBytes, etcE3 + 4);
        diskBytes[etcE3 + 6] = 11;
        diskBytes[etcE3 + 7] = 1;
        Encoding.UTF8.GetBytes("config.json").CopyTo(diskBytes, etcE3 + 8);

        // Block 13: /etc/config.json Content
        int file2DataOffset = partOffset + 13 * (int)blockSize;
        file2Bytes.CopyTo(diskBytes, file2DataOffset);

        File.WriteAllBytes(outputPath, diskBytes);
    }
}
