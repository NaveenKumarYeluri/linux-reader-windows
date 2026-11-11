using System.Text;
using LinuxReader.Core.Ext4;
using Xunit;

namespace LinuxReader.Tests;

public class Ext4Tests
{
    [Fact]
    public void Superblock_ValidatesMagicAndParsesFields()
    {
        byte[] disk = new byte[4096];
        int sbOffset = 1024;

        // Magic 0xEF53 at offset 56
        BitConverter.GetBytes(Ext4Superblock.EXT4_MAGIC).CopyTo(disk, sbOffset + 56);
        // Inodes count: 1000 at offset 0
        BitConverter.GetBytes(1000u).CopyTo(disk, sbOffset + 0);
        // Blocks count lo: 8000 at offset 4
        BitConverter.GetBytes(8000u).CopyTo(disk, sbOffset + 4);
        // Log block size: 2 (1024 << 2 = 4096) at offset 24
        BitConverter.GetBytes(2u).CopyTo(disk, sbOffset + 24);
        // Blocks per group: 32768 at offset 32
        BitConverter.GetBytes(32768u).CopyTo(disk, sbOffset + 32);
        // Inodes per group: 8192 at offset 40
        BitConverter.GetBytes(8192u).CopyTo(disk, sbOffset + 40);
        // Inode size: 256 at offset 88
        BitConverter.GetBytes((ushort)256).CopyTo(disk, sbOffset + 88);
        // Volume name at offset 120
        Encoding.ASCII.GetBytes("UbuntuRoot").CopyTo(disk, sbOffset + 120);

        using var stream = new MemoryStream(disk);
        bool success = Ext4Superblock.TryRead(stream, 0, out var sb);

        Assert.True(success);
        Assert.True(sb.IsValid);
        Assert.Equal(4096u, sb.BlockSize);
        Assert.Equal(1000u, sb.InodesCount);
        Assert.Equal(8000ul, sb.BlocksCount);
        Assert.Equal(256, sb.InodeSize);
        Assert.Equal("UbuntuRoot", sb.VolumeName);
    }

    [Fact]
    public void Inode_ParsesPermissionsAndTypes()
    {
        byte[] inodeData = new byte[128];
        // Directory 0x4000 | 0755 (rwxr-xr-x)
        ushort dirMode = 0x4000 | 0x01ED;
        BitConverter.GetBytes(dirMode).CopyTo(inodeData, 0);
        BitConverter.GetBytes(4096u).CopyTo(inodeData, 4); // Size = 4096

        var inode = Ext4Inode.Parse(inodeData, 0, 128, 2);

        Assert.True(inode.IsDirectory);
        Assert.False(inode.IsRegularFile);
        Assert.Equal(4096ul, inode.Size);
        Assert.Equal("drwxr-xr-x", inode.PermissionsString);

        // Regular file 0x8000 | 0644 (rw-r--r--)
        ushort fileMode = 0x8000 | 0x01A4;
        BitConverter.GetBytes(fileMode).CopyTo(inodeData, 0);
        BitConverter.GetBytes(12345u).CopyTo(inodeData, 4);

        var fileInode = Ext4Inode.Parse(inodeData, 0, 128, 12);

        Assert.False(fileInode.IsDirectory);
        Assert.True(fileInode.IsRegularFile);
        Assert.Equal(12345ul, fileInode.Size);
        Assert.Equal("-rw-r--r--", fileInode.PermissionsString);
    }

    [Fact]
    public void EndToEnd_SyntheticExt4Filesystem_MountsAndExtractsFile()
    {
        uint blockSize = 1024;
        uint totalBlocks = 64;
        byte[] fsBytes = new byte[totalBlocks * blockSize];

        // 1. Superblock at block 1 (byte 1024)
        int sbOffset = (int)blockSize;
        BitConverter.GetBytes(Ext4Superblock.EXT4_MAGIC).CopyTo(fsBytes, sbOffset + 56);
        BitConverter.GetBytes(32u).CopyTo(fsBytes, sbOffset + 0);  // inodes count
        BitConverter.GetBytes(totalBlocks).CopyTo(fsBytes, sbOffset + 4); // blocks count
        BitConverter.GetBytes(1u).CopyTo(fsBytes, sbOffset + 20); // first data block = 1
        BitConverter.GetBytes(0u).CopyTo(fsBytes, sbOffset + 24); // log block size = 0 (1024 B)
        BitConverter.GetBytes(totalBlocks).CopyTo(fsBytes, sbOffset + 32); // blocks per group
        BitConverter.GetBytes(32u).CopyTo(fsBytes, sbOffset + 40); // inodes per group
        BitConverter.GetBytes((ushort)128).CopyTo(fsBytes, sbOffset + 88); // inode size
        BitConverter.GetBytes(Ext4Superblock.INCOMPAT_EXTENTS | Ext4Superblock.INCOMPAT_FILETYPE).CopyTo(fsBytes, sbOffset + 96);
        Encoding.ASCII.GetBytes("TestExt4").CopyTo(fsBytes, sbOffset + 120);

        // 2. Block Group Descriptor at block 2 (byte 2048)
        int bgOffset = 2 * (int)blockSize;
        BitConverter.GetBytes(5u).CopyTo(fsBytes, bgOffset + 8); // bg_inode_table_lo = block 5

        // Inode Table is at block 5 (byte 5120)
        int inodeTableOffset = 5 * (int)blockSize;

        // 3. Root Inode (Inode #2) is at index 1: offset = 5120 + 1 * 128 = 5248
        int rootInodeOffset = inodeTableOffset + 1 * 128;
        BitConverter.GetBytes((ushort)(0x4000 | 0x01ED)).CopyTo(fsBytes, rootInodeOffset + 0); // Directory
        BitConverter.GetBytes(blockSize).CopyTo(fsBytes, rootInodeOffset + 4); // Size = 1024
        BitConverter.GetBytes(Ext4Inode.EXT4_EXTENTS_FL).CopyTo(fsBytes, rootInodeOffset + 32); // Use extents

        // Root Inode Extents: block 10 holds directory entries
        int rootExtentOffset = rootInodeOffset + 40;
        BitConverter.GetBytes((ushort)0xF30A).CopyTo(fsBytes, rootExtentOffset + 0); // magic
        BitConverter.GetBytes((ushort)1).CopyTo(fsBytes, rootExtentOffset + 2);      // 1 entry
        BitConverter.GetBytes((ushort)4).CopyTo(fsBytes, rootExtentOffset + 4);      // max 4
        BitConverter.GetBytes((ushort)0).CopyTo(fsBytes, rootExtentOffset + 6);      // depth 0
        // Leaf extent: logical 0, len 1, physical block 10
        BitConverter.GetBytes(0u).CopyTo(fsBytes, rootExtentOffset + 12);
        BitConverter.GetBytes((ushort)1).CopyTo(fsBytes, rootExtentOffset + 16);
        BitConverter.GetBytes(10u).CopyTo(fsBytes, rootExtentOffset + 20);

        // 4. File Inode (Inode #12) is at index 11: offset = 5120 + 11 * 128 = 6528
        string testContent = "Hello Linux from Windows!";
        byte[] contentBytes = Encoding.UTF8.GetBytes(testContent);

        int fileInodeOffset = inodeTableOffset + 11 * 128;
        BitConverter.GetBytes((ushort)(0x8000 | 0x01A4)).CopyTo(fsBytes, fileInodeOffset + 0); // Regular file
        BitConverter.GetBytes((uint)contentBytes.Length).CopyTo(fsBytes, fileInodeOffset + 4); // File size
        BitConverter.GetBytes(Ext4Inode.EXT4_EXTENTS_FL).CopyTo(fsBytes, fileInodeOffset + 32);

        // File Inode Extents: block 11 holds file content
        int fileExtentOffset = fileInodeOffset + 40;
        BitConverter.GetBytes((ushort)0xF30A).CopyTo(fsBytes, fileExtentOffset + 0);
        BitConverter.GetBytes((ushort)1).CopyTo(fsBytes, fileExtentOffset + 2);
        BitConverter.GetBytes((ushort)4).CopyTo(fsBytes, fileExtentOffset + 4);
        BitConverter.GetBytes((ushort)0).CopyTo(fsBytes, fileExtentOffset + 6);
        // Leaf extent: logical 0, len 1, physical block 11
        BitConverter.GetBytes(0u).CopyTo(fsBytes, fileExtentOffset + 12);
        BitConverter.GetBytes((ushort)1).CopyTo(fsBytes, fileExtentOffset + 16);
        BitConverter.GetBytes(11u).CopyTo(fsBytes, fileExtentOffset + 20);

        // 5. Root Directory Data at block 10 (byte 10240)
        int dirDataOffset = 10 * (int)blockSize;
        // Entry 1: "." (inode 2, rec_len 12, name_len 1, type 2)
        BitConverter.GetBytes(2u).CopyTo(fsBytes, dirDataOffset + 0);
        BitConverter.GetBytes((ushort)12).CopyTo(fsBytes, dirDataOffset + 4);
        fsBytes[dirDataOffset + 6] = 1;
        fsBytes[dirDataOffset + 7] = 2;
        fsBytes[dirDataOffset + 8] = (byte)'.';

        // Entry 2: ".." (inode 2, rec_len 12, name_len 2, type 2)
        int e2 = dirDataOffset + 12;
        BitConverter.GetBytes(2u).CopyTo(fsBytes, e2 + 0);
        BitConverter.GetBytes((ushort)12).CopyTo(fsBytes, e2 + 4);
        fsBytes[e2 + 6] = 2;
        fsBytes[e2 + 7] = 2;
        fsBytes[e2 + 8] = (byte)'.';
        fsBytes[e2 + 9] = (byte)'.';

        // Entry 3: "hello.txt" (inode 12, rec_len = 1024 - 24 = 1000, name_len 9, type 1)
        int e3 = dirDataOffset + 24;
        BitConverter.GetBytes(12u).CopyTo(fsBytes, e3 + 0);
        BitConverter.GetBytes((ushort)1000).CopyTo(fsBytes, e3 + 4);
        fsBytes[e3 + 6] = 9;
        fsBytes[e3 + 7] = 1; // regular file
        Encoding.UTF8.GetBytes("hello.txt").CopyTo(fsBytes, e3 + 8);

        // 6. File Content Data at block 11 (byte 11264)
        int fileDataOffset = 11 * (int)blockSize;
        contentBytes.CopyTo(fsBytes, fileDataOffset);

        // --- Now run tests on this synthetic disk stream ---
        using var stream = new MemoryStream(fsBytes);
        using var fs = Ext4FileSystem.Open(stream);

        Assert.Equal("TestExt4", fs.Superblock.VolumeName);
        Assert.Equal(1024u, fs.BlockSize);

        // Test directory listing
        var items = fs.ListDirectory("/");
        Assert.Single(items);
        Assert.Equal("hello.txt", items[0].Name);
        Assert.True(items[0].IsRegularFile);
        Assert.Equal((ulong)contentBytes.Length, items[0].Size);

        // Test reading file content
        using (var fileStream = fs.OpenFile(items[0].Inode))
        using (var reader = new StreamReader(fileStream, Encoding.UTF8))
        {
            string readText = reader.ReadToEnd();
            Assert.Equal(testContent, readText);
        }

        // Test extracting file to Windows temp folder
        string tempExtractedFile = Path.Combine(Path.GetTempPath(), $"linux_test_{Guid.NewGuid():N}.txt");
        try
        {
            fs.ExtractFile(items[0].Inode, tempExtractedFile);
            Assert.True(File.Exists(tempExtractedFile));
            string extractedText = File.ReadAllText(tempExtractedFile);
            Assert.Equal(testContent, extractedText);
        }
        finally
        {
            if (File.Exists(tempExtractedFile))
            {
                File.Delete(tempExtractedFile);
            }
        }
    }
}
