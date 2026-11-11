namespace LinuxReader.Core.Ext4;

public record Ext4ExtentRange(ulong LogicalBlock, ulong PhysicalBlock, uint BlockCount);
