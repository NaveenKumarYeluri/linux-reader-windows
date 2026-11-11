using System.Text;
using LinuxReader.Core.Ext4;
using LinuxReader.Core.IO;
using LinuxReader.Core.Partitions;

namespace LinuxReader.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "list-drives" or "drives" => ListDrives(),
                "list-partitions" or "partitions" => ListPartitions(args),
                "info" => ShowInfo(args),
                "ls" or "list" => ListDirectory(args),
                "cat" or "read" => CatFile(args),
                "extract" => ExtractItem(args),
                "create-sample" or "create-test-image" => CreateSampleImage(args),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[Error]: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("===============================================================");
        Console.WriteLine(" Linux File Reader for Windows (Strict Read-Only)");
        Console.WriteLine("===============================================================");
        Console.WriteLine("Usage: LinuxReader.Cli <command> [arguments]\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  list-drives                         Lists physical drives on this machine.");
        Console.WriteLine("  list-partitions <disk-or-image>     Scans GPT/MBR partitions.");
        Console.WriteLine("  info <disk-or-image> [partitionIdx] Inspects Ext4 superblock and metadata.");
        Console.WriteLine("  ls <disk-or-image> [partIdx] [path] Lists contents of a directory (e.g. /).");
        Console.WriteLine("  cat <disk-or-image> [partIdx] <path>Prints text file contents to stdout.");
        Console.WriteLine("  extract <disk-or-image> [partIdx] <linuxPath> <destPath>");
        Console.WriteLine("                                      Extracts file or folder to Windows.");
        Console.WriteLine("  create-sample <outputPath>          Generates a sample GPT/Ext4 disk image.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  LinuxReader.Cli list-drives");
        Console.WriteLine("  LinuxReader.Cli list-partitions \\\\.\\PhysicalDrive0");
        Console.WriteLine("  LinuxReader.Cli list-partitions ubuntu-disk.img");
        Console.WriteLine("  LinuxReader.Cli ls ubuntu-disk.img 1 /etc");
        Console.WriteLine("  LinuxReader.Cli extract ubuntu-disk.img 1 /etc/hosts C:\\Extracted\\hosts");
        Console.WriteLine("===============================================================");
    }

    private static int ListDrives()
    {
        Console.WriteLine("Scanning physical drives (GENERIC_READ mode)...");
        var drives = DeviceManager.GetPhysicalDrives();

        if (drives.Count == 0)
        {
            Console.WriteLine("No physical drives detected (or insufficient permissions).");
            return 0;
        }

        Console.WriteLine($"{"Index",-6} {"Device Path",-24} {"Size",-12} {"Status",-15}");
        Console.WriteLine(new string('-', 60));

        foreach (var d in drives)
        {
            string status = d.IsAccessible ? "Ready (Read-Only)" : "Access Denied";
            Console.WriteLine($"{d.Index,-6} {d.DevicePath,-24} {d.FormattedSize,-12} {status,-15}");
            if (!string.IsNullOrEmpty(d.StatusMessage))
            {
                Console.WriteLine($"       -> {d.StatusMessage}");
            }
        }

        return 0;
    }

    private static int ListPartitions(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Error: Please provide a disk path or image file path.");
            return 1;
        }

        string source = args[1];
        using var stream = OpenSourceStream(source);
        var partitions = PartitionScanner.Scan(stream);

        Console.WriteLine($"Partitions for: {source}");
        Console.WriteLine($"Total Partitions: {partitions.Count}\n");

        if (partitions.Count == 0)
        {
            Console.WriteLine("No recognizable GPT, MBR, or raw Ext4 partitions found.");
            return 0;
        }

        Console.WriteLine($"{"#",-4} {"Name",-32} {"Type",-8} {"Start LBA",-12} {"Size",-12} {"Linux?"}");
        Console.WriteLine(new string('-', 80));

        foreach (var p in partitions)
        {
            string linuxFlag = p.IsLinux ? "YES [Ext4/Linux]" : "No";
            Console.WriteLine($"{p.PartitionIndex,-4} {Truncate(p.Name, 31),-32} {p.PartitionType,-8} {p.StartSector,-12} {p.FormattedSize,-12} {linuxFlag}");
        }

        return 0;
    }

    private static int ShowInfo(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Error: Please specify target image or drive.");
            return 1;
        }

        string source = args[1];
        int partIndex = args.Length > 2 && int.TryParse(args[2], out int p) ? p : 1;

        using var stream = OpenSourceStream(source);
        using var partStream = GetPartitionStream(stream, partIndex);

        if (!Ext4Superblock.TryRead(partStream, 0, out var sb) || !sb.IsValid)
        {
            Console.Error.WriteLine("Partition does not contain a valid Ext2/Ext3/Ext4 superblock.");
            return 1;
        }

        Console.WriteLine("==================================================");
        Console.WriteLine(" Ext4 Superblock Information");
        Console.WriteLine("==================================================");
        Console.WriteLine($"Volume Name:          {(string.IsNullOrWhiteSpace(sb.VolumeName) ? "<unnamed>" : sb.VolumeName)}");
        Console.WriteLine($"UUID:                 {sb.Uuid}");
        Console.WriteLine($"Block Size:           {sb.BlockSize} bytes");
        Console.WriteLine($"Total Blocks:         {sb.BlocksCount:N0}");
        Console.WriteLine($"Total Inodes:         {sb.InodesCount:N0}");
        Console.WriteLine($"Blocks Per Group:     {sb.BlocksPerGroup:N0}");
        Console.WriteLine($"Inodes Per Group:     {sb.InodesPerGroup:N0}");
        Console.WriteLine($"Inode Size:           {sb.InodeSize} bytes");
        Console.WriteLine($"Extents Enabled:      {sb.HasExtents}");
        Console.WriteLine($"64-Bit Mode:          {sb.Is64Bit}");
        Console.WriteLine($"Flex BG Enabled:      {sb.HasFlexBg}");
        Console.WriteLine($"Last Mounted Path:    {sb.LastMountedPath}");
        Console.WriteLine("==================================================");

        return 0;
    }

    private static int ListDirectory(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Error: Please specify target image or drive.");
            return 1;
        }

        string source = args[1];
        int partIndex = 1;
        string path = "/";

        if (args.Length > 2)
        {
            if (int.TryParse(args[2], out int p))
            {
                partIndex = p;
                if (args.Length > 3) path = args[3];
            }
            else
            {
                path = args[2];
            }
        }

        using var stream = OpenSourceStream(source);
        using var partStream = GetPartitionStream(stream, partIndex);
        using var fs = Ext4FileSystem.Open(partStream);

        var items = fs.ListDirectory(path);

        Console.WriteLine($"Directory listing for: {path} ({items.Count} items)\n");
        Console.WriteLine($"{"Permissions",-12} {"Size",-12} {"Modified",-22} {"Name"}");
        Console.WriteLine(new string('-', 70));

        foreach (var item in items)
        {
            string name = item.IsDirectory ? $"{item.Name}/" : item.Name;
            if (item.IsSymlink && !string.IsNullOrEmpty(item.SymlinkTarget))
            {
                name += $" -> {item.SymlinkTarget}";
            }

            Console.WriteLine($"{item.Permissions,-12} {item.FormattedSize,-12} {item.ModifiedTime.LocalDateTime:yyyy-MM-dd HH:mm:ss} {name}");
        }

        return 0;
    }

    private static int CatFile(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: cat <disk-or-image> [partitionIdx] <linuxFilePath>");
            return 1;
        }

        string source = args[1];
        int partIndex = 1;
        string filePath = args[2];

        if (int.TryParse(args[2], out int p) && args.Length > 3)
        {
            partIndex = p;
            filePath = args[3];
        }

        using var stream = OpenSourceStream(source);
        using var partStream = GetPartitionStream(stream, partIndex);
        using var fs = Ext4FileSystem.Open(partStream);

        using var fileStream = fs.OpenFile(filePath);
        using var reader = new StreamReader(fileStream, Encoding.UTF8);
        string content = reader.ReadToEnd();
        Console.Write(content);

        return 0;
    }

    private static int ExtractItem(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: extract <disk-or-image> [partIdx] <linuxPath> <destPath>");
            return 1;
        }

        string source = args[1];
        int partIndex = 1;
        string linuxPath = args[2];
        string destPath = args[3];

        if (int.TryParse(args[2], out int p) && args.Length > 4)
        {
            partIndex = p;
            linuxPath = args[3];
            destPath = args[4];
        }

        using var stream = OpenSourceStream(source);
        using var partStream = GetPartitionStream(stream, partIndex);
        using var fs = Ext4FileSystem.Open(partStream);

        var inode = fs.ResolvePath(linuxPath);
        if (inode == null)
        {
            Console.Error.WriteLine($"Path not found: {linuxPath}");
            return 1;
        }

        var progress = new Progress<string>(msg => Console.WriteLine($"[Progress] {msg}"));

        if (inode.IsDirectory)
        {
            Console.WriteLine($"Extracting directory {linuxPath} to {destPath}...");
            fs.ExtractDirectory(inode, destPath, progress);
        }
        else
        {
            Console.WriteLine($"Extracting file {linuxPath} to {destPath}...");
            fs.ExtractFile(inode, destPath);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Extraction completed successfully.");
        Console.ResetColor();

        return 0;
    }

    private static int CreateSampleImage(string[] args)
    {
        string outputPath = args.Length > 1 ? args[1] : "sample-linux-disk.img";
        Console.WriteLine($"Generating synthetic GPT / Ext4 disk image: {outputPath}...");
        LinuxReader.Core.SampleImageGenerator.GenerateSampleDiskImage(outputPath);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Sample disk image generated successfully ({new FileInfo(outputPath).Length:N0} bytes).");
        Console.ResetColor();
        return 0;
    }

    private static Stream OpenSourceStream(string source)
    {
        if (source.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
        {
            return SafeDiskStream.OpenPhysicalDrive(source);
        }

        return SafeDiskStream.OpenFileImage(source);
    }

    private static Stream GetPartitionStream(Stream baseStream, int partitionIndex)
    {
        var partitions = PartitionScanner.Scan(baseStream);
        if (partitions.Count == 0)
        {
            // Fallback: entire stream
            return new SubStream(baseStream, 0, baseStream.Length, leaveOpen: true);
        }

        var selected = partitions.FirstOrDefault(p => p.PartitionIndex == partitionIndex)
                       ?? partitions.FirstOrDefault(p => p.IsLinux)
                       ?? partitions[0];

        return new SubStream(baseStream, selected.StartOffset, selected.Length, leaveOpen: true);
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text[..(max - 3)] + "...";
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }
}
