using System.Text;

namespace LinuxReader.Core.Ext4;

public static class Ext4DirectoryReader
{
    public static List<Ext4DirectoryEntry> ParseEntries(byte[] blockData)
    {
        var entries = new List<Ext4DirectoryEntry>();
        int offset = 0;

        while (offset + 8 <= blockData.Length)
        {
            uint inode = BitConverter.ToUInt32(blockData, offset + 0);
            ushort recLen = BitConverter.ToUInt16(blockData, offset + 4);
            byte nameLen = blockData[offset + 6];
            byte fileType = blockData[offset + 7];

            // Protect against corrupted record lengths (infinite loops or negative steps)
            if (recLen < 8 || offset + recLen > blockData.Length)
            {
                break;
            }

            if (inode != 0 && nameLen > 0 && offset + 8 + nameLen <= blockData.Length)
            {
                string name = Encoding.UTF8.GetString(blockData, offset + 8, nameLen);

                // Exclude '.' and '..' for directory traversal listings if desired, or keep them accessible
                entries.Add(new Ext4DirectoryEntry
                {
                    InodeNumber = inode,
                    Name = name,
                    FileType = fileType
                });
            }

            offset += recLen;
        }

        return entries;
    }
}
