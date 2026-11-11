using LinuxReader.Core.IO;
using Xunit;

namespace LinuxReader.Tests;

public class SubStreamTests
{
    [Fact]
    public void SubStream_ReadsCorrectWindow_AndEnforcesReadOnly()
    {
        byte[] original = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        using var baseStream = new MemoryStream(original);

        // Window of 5 bytes starting at offset 3: [3, 4, 5, 6, 7]
        using var subStream = new SubStream(baseStream, 3, 5);

        Assert.Equal(5, subStream.Length);
        Assert.True(subStream.CanRead);
        Assert.True(subStream.CanSeek);
        Assert.False(subStream.CanWrite);

        byte[] buffer = new byte[5];
        int read = subStream.Read(buffer, 0, 5);

        Assert.Equal(5, read);
        Assert.Equal(new byte[] { 3, 4, 5, 6, 7 }, buffer);

        // Attempting to read past length returns 0
        Assert.Equal(0, subStream.Read(buffer, 0, 1));

        // Write must throw NotSupportedException
        Assert.Throws<NotSupportedException>(() => subStream.Write(buffer, 0, 1));
        Assert.Throws<NotSupportedException>(() => subStream.SetLength(10));
    }

    [Fact]
    public void SubStream_SeekWithinBounds()
    {
        byte[] original = new byte[] { 10, 20, 30, 40, 50, 60 };
        using var baseStream = new MemoryStream(original);
        using var subStream = new SubStream(baseStream, 1, 4); // [20, 30, 40, 50]

        subStream.Seek(2, SeekOrigin.Begin);
        Assert.Equal(2, subStream.Position);
        Assert.Equal(40, subStream.ReadByte());

        subStream.Seek(-1, SeekOrigin.Current);
        Assert.Equal(40, subStream.ReadByte());

        // Seek past bounds must throw IOException
        Assert.Throws<IOException>(() => subStream.Seek(10, SeekOrigin.Begin));
    }
}
