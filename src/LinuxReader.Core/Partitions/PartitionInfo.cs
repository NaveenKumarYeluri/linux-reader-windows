namespace LinuxReader.Core.Partitions;

public record PartitionInfo(
    int PartitionIndex,
    string Name,
    string PartitionType,
    long StartSector,
    long SectorCount,
    long StartOffset,
    long Length,
    string FormattedSize,
    bool IsLinux,
    string FileSystemType);
