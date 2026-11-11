namespace LinuxReader.Core.IO;

public record PhysicalDriveInfo(
    int Index,
    string DevicePath,
    string FriendlyName,
    long SizeBytes,
    string FormattedSize,
    bool IsAccessible,
    string? StatusMessage = null);
