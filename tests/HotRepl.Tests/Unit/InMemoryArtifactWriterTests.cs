using System.IO;
using System.Text;
using System.Threading.Tasks;
using HotRepl.Control.Artifacts;
using Xunit;

namespace HotRepl.Tests.Unit;

public class InMemoryArtifactWriterTests
{
    [Fact]
    public async Task WriteAsync_ProducesArtifactRefWithCorrectHash()
    {
        var writer = new InMemoryArtifactWriter();
        var bytes = Encoding.UTF8.GetBytes("hello world");

        var artifact = await writer.WriteAsync("greeting", bytes, "text/plain");

        Assert.Equal("greeting", artifact.LogicalName);
        Assert.Equal(bytes.Length, artifact.ByteSize);
        Assert.Equal("text/plain", artifact.ContentType);
        Assert.True(artifact.Finalized);
        // sha256("hello world")
        Assert.Equal(
            "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
            artifact.Sha256
        );
    }

    [Fact]
    public async Task WriteAsync_SameLogicalName_ReplacesPrevious()
    {
        var writer = new InMemoryArtifactWriter();
        await writer.WriteAsync("data", new byte[] { 1, 2, 3 });
        var second = await writer.WriteAsync("data", new byte[] { 4, 5, 6, 7 });

        Assert.Equal(4, second.ByteSize);
        Assert.Single(writer.Snapshot()); // exactly one entry
    }

    [Fact]
    public async Task WriteStreamAsync_ReadsToEndAndProducesRef()
    {
        var writer = new InMemoryArtifactWriter();
        var stream = new MemoryStream(new byte[] { 10, 20, 30 });

        var artifact = await writer.WriteStreamAsync("stream", stream);

        Assert.Equal(3, artifact.ByteSize);
    }
}
