using System.Text;

namespace LinuxReader.Core.Ext4;

public sealed class Ext4FileSystem : IDisposable
{
    private readonly Stream _stream;
    private readonly Ext4Superblock _superblock;
    private readonly List<Ext4BlockGroupDescriptor> _blockGroups;
    private readonly Ext4BlockReader _blockReader;
    private bool _disposed;

    public Ext4Superblock Superblock => _superblock;
    public uint BlockSize => _superblock.BlockSize;

    private Ext4FileSystem(Stream stream, Ext4Superblock superblock, List<Ext4BlockGroupDescriptor> blockGroups)
    {
        _stream = stream;
        _superblock = superblock;
        _blockGroups = blockGroups;
        _blockReader = new Ext4BlockReader(stream, superblock.BlockSize);
    }

    public static Ext4FileSystem Open(Stream partitionStream)
    {
        ArgumentNullException.ThrowIfNull(partitionStream);

        if (!Ext4Superblock.TryRead(partitionStream, 0, out var sb) || !sb.IsValid)
        {
            throw new InvalidOperationException("Not a valid Ext2/Ext3/Ext4 filesystem (magic mismatch).");
        }

        // Calculate number of block groups
        uint numGroups = (uint)((sb.BlocksCount + sb.BlocksPerGroup - 1) / sb.BlocksPerGroup);
        int descSize = sb.DescriptorSize;

        // Block group descriptor table starts immediately after the superblock block
        ulong bgdtBlock = sb.FirstDataBlock + 1;
        long bgdtOffset = (long)(bgdtBlock * sb.BlockSize);

        byte[] bgdtBuffer = new byte[numGroups * descSize];
        partitionStream.Seek(bgdtOffset, SeekOrigin.Begin);
        int read = partitionStream.Read(bgdtBuffer, 0, bgdtBuffer.Length);
        if (read < bgdtBuffer.Length)
        {
            throw new EndOfStreamException("Unable to read entire block group descriptor table.");
        }

        var blockGroups = new List<Ext4BlockGroupDescriptor>((int)numGroups);
        for (uint i = 0; i < numGroups; i++)
        {
            int offset = (int)(i * descSize);
            blockGroups.Add(Ext4BlockGroupDescriptor.Parse(bgdtBuffer, offset, descSize, i));
        }

        return new Ext4FileSystem(partitionStream, sb, blockGroups);
    }

    public Ext4Inode GetRootInode()
    {
        return GetInode(Ext4Inode.ROOT_INODE);
    }

    public Ext4Inode GetInode(uint inodeNumber)
    {
        EnsureNotDisposed();

        if (inodeNumber == 0 || inodeNumber > _superblock.InodesCount)
        {
            throw new ArgumentOutOfRangeException(nameof(inodeNumber), $"Invalid inode number: {inodeNumber}");
        }

        uint group = (inodeNumber - 1) / _superblock.InodesPerGroup;
        uint index = (inodeNumber - 1) % _superblock.InodesPerGroup;

        if (group >= _blockGroups.Count)
        {
            throw new InvalidOperationException($"Inode {inodeNumber} belongs to non-existent group {group}");
        }

        var bg = _blockGroups[(int)group];
        long offset = (long)(bg.InodeTableBlock * _superblock.BlockSize) + (long)(index * _superblock.InodeSize);

        byte[] buffer = new byte[_superblock.InodeSize];
        lock (_stream)
        {
            _stream.Seek(offset, SeekOrigin.Begin);
            int r = _stream.Read(buffer, 0, _superblock.InodeSize);
            if (r < _superblock.InodeSize)
            {
                throw new EndOfStreamException($"Failed to read inode {inodeNumber} at offset {offset}");
            }
        }

        return Ext4Inode.Parse(buffer, 0, _superblock.InodeSize, inodeNumber);
    }

    public List<Ext4Item> ListDirectory(Ext4Inode dirInode, string parentPath = "")
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(dirInode);

        if (!dirInode.IsDirectory)
        {
            throw new InvalidOperationException($"Inode {dirInode.InodeNumber} is not a directory.");
        }

        var extents = _blockReader.GetExtents(dirInode);
        using var dirStream = new Ext4FileStream(_stream, _superblock.BlockSize, dirInode, extents);

        byte[] dirData = new byte[dirStream.Length];
        int read = dirStream.Read(dirData, 0, dirData.Length);

        var entries = Ext4DirectoryReader.ParseEntries(dirData);
        var items = new List<Ext4Item>();

        string cleanParent = parentPath.TrimEnd('/');

        foreach (var entry in entries)
        {
            // Skip self and parent directory navigation entries in listing
            if (entry.Name == "." || entry.Name == "..")
            {
                continue;
            }

            try
            {
                var childInode = GetInode(entry.InodeNumber);
                string itemPath = $"{cleanParent}/{entry.Name}";

                string? symlinkTarget = null;
                if (childInode.IsSymlink)
                {
                    symlinkTarget = ReadSymlinkTarget(childInode);
                }

                items.Add(new Ext4Item
                {
                    Name = entry.Name,
                    FullPath = itemPath,
                    InodeNumber = entry.InodeNumber,
                    Inode = childInode,
                    SymlinkTarget = symlinkTarget
                });
            }
            catch
            {
                // Gracefully ignore corrupt child inodes
            }
        }

        // Sort: directories first, then alphabetical
        items.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return items;
    }

    public List<Ext4Item> ListDirectory(string path)
    {
        var inode = ResolvePath(path);
        if (inode == null)
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
        return ListDirectory(inode, path);
    }

    public Ext4Inode? ResolvePath(string path)
    {
        EnsureNotDisposed();
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return GetRootInode();
        }

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = GetRootInode();

        foreach (var segment in segments)
        {
            if (!current.IsDirectory)
            {
                return null;
            }

            var children = ListDirectory(current);
            var match = children.FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));
            if (match == null)
            {
                return null;
            }

            current = match.Inode;
        }

        return current;
    }

    public Stream OpenFile(Ext4Inode fileInode)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(fileInode);

        var extents = _blockReader.GetExtents(fileInode);
        return new Ext4FileStream(_stream, _superblock.BlockSize, fileInode, extents);
    }

    public Stream OpenFile(string path)
    {
        var inode = ResolvePath(path);
        if (inode == null || !inode.IsRegularFile)
        {
            throw new FileNotFoundException($"File not found: {path}", path);
        }

        return OpenFile(inode);
    }

    public string ReadSymlinkTarget(Ext4Inode symlinkInode)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(symlinkInode);

        if (!symlinkInode.IsSymlink)
        {
            return string.Empty;
        }

        // Fast symlink: path length < 60 bytes stored directly in BlockData
        if (symlinkInode.Size < 60)
        {
            int len = (int)Math.Min((ulong)symlinkInode.BlockData.Length, symlinkInode.Size);
            return Encoding.UTF8.GetString(symlinkInode.BlockData, 0, len);
        }

        // Slow symlink: read from file stream
        using var s = OpenFile(symlinkInode);
        using var reader = new StreamReader(s, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Safely extracts a file from the Linux filesystem to the Windows destination.
    /// Preserves original modification timestamps. Strictly read-only on the source.
    /// </summary>
    public void ExtractFile(Ext4Inode fileInode, string destinationWindowsPath)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(fileInode);
        ArgumentNullException.ThrowIfNull(destinationWindowsPath);

        string? dir = Path.GetDirectoryName(destinationWindowsPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using (var sourceStream = OpenFile(fileInode))
        using (var destStream = new FileStream(destinationWindowsPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            sourceStream.CopyTo(destStream);
        }

        // Set modification time
        try
        {
            File.SetLastWriteTimeUtc(destinationWindowsPath, fileInode.ModificationTime.UtcDateTime);
        }
        catch
        {
            // Ignore if OS does not allow setting time on certain destinations
        }
    }

    /// <summary>
    /// Safely extracts an entire directory hierarchy into a Windows folder.
    /// </summary>
    public void ExtractDirectory(Ext4Inode dirInode, string destinationWindowsFolder, IProgress<string>? progress = null)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(dirInode);
        ArgumentNullException.ThrowIfNull(destinationWindowsFolder);

        if (!Directory.Exists(destinationWindowsFolder))
        {
            Directory.CreateDirectory(destinationWindowsFolder);
        }

        var items = ListDirectory(dirInode);
        foreach (var item in items)
        {
            string destPath = Path.Combine(destinationWindowsFolder, SanitizeWindowsFileName(item.Name));

            if (item.IsDirectory)
            {
                progress?.Report($"Creating directory {item.Name}...");
                ExtractDirectory(item.Inode, destPath, progress);
            }
            else if (item.IsRegularFile)
            {
                progress?.Report($"Extracting {item.Name} ({item.FormattedSize})...");
                ExtractFile(item.Inode, destPath);
            }
        }
    }

    private static string SanitizeWindowsFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(invalidChars.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Ext4FileSystem));
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
