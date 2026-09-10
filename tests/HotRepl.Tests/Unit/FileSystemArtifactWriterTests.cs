using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HotRepl.Control.Artifacts;
using Xunit;

namespace HotRepl.Tests.Unit;

public class FileSystemArtifactWriterTests
{
    [Fact]
    public async Task AttachBytesAsync_WritesFileAndReferencesItByPath()
    {
        var directory = TestArtifacts.Directory();
        try
        {
            var writer = new FileSystemArtifactWriter(directory);
            var bytes = Encoding.UTF8.GetBytes("hello world");

            var artifact = await writer.AttachBytesAsync("greeting", bytes, "text/plain");

            Assert.Equal("greeting", artifact.LogicalName);
            Assert.Equal(bytes.Length, artifact.ByteSize);
            Assert.Equal("text/plain", artifact.ContentType);
            Assert.True(artifact.Finalized);
            Assert.NotNull(artifact.Path);
            Assert.StartsWith("file://", artifact.Uri, StringComparison.Ordinal);
            // sha256("hello world")
            Assert.Equal(
                "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
                artifact.Sha256
            );
            Assert.Equal(bytes, await File.ReadAllBytesAsync(artifact.Path!));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task AttachStreamAsync_WritesEveryChunkItRead()
    {
        var directory = TestArtifacts.Directory();
        try
        {
            var writer = new FileSystemArtifactWriter(directory);
            var payload = new byte[200_000];
            new Random(1).NextBytes(payload);

            var artifact = await writer.AttachStreamAsync("stream", new MemoryStream(payload));

            Assert.Equal(payload.Length, artifact.ByteSize);
            Assert.Equal(payload, await File.ReadAllBytesAsync(artifact.Path!));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task AttachBytesAsync_SameLogicalName_ReplacesFileContent()
    {
        var directory = TestArtifacts.Directory();
        try
        {
            var writer = new FileSystemArtifactWriter(directory);
            await writer.AttachBytesAsync("data", new byte[] { 1, 2, 3 });
            var second = await writer.AttachBytesAsync("data", new byte[] { 4, 5, 6, 7 });

            Assert.Equal(4, second.ByteSize);
            Assert.Equal(new byte[] { 4, 5, 6, 7 }, await File.ReadAllBytesAsync(second.Path!));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task AttachBytesAsync_LogicalNameWithSeparators_StaysInsideDirectory()
    {
        var directory = TestArtifacts.Directory();
        try
        {
            var writer = new FileSystemArtifactWriter(directory);

            var artifact = await writer.AttachBytesAsync("../../escape", new byte[] { 1 });

            Assert.Equal(directory, Path.GetDirectoryName(artifact.Path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Constructor_CreatesNoDirectoryUntilAnAttachment()
    {
        var directory = TestArtifacts.Directory();

        _ = new FileSystemArtifactWriter(directory);

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void ScopeDirectory_ClientSuppliedScope_CannotTraverseOutOfRoot()
    {
        var root = TestArtifacts.Directory();

        var scoped = FileSystemArtifactWriter.ScopeDirectory(root, "../../etc");

        Assert.Equal(root, Path.GetDirectoryName(scoped));
    }

    [Fact]
    public async Task AttachFileAsync_ReferencesTheExistingFileInPlace()
    {
        var directory = TestArtifacts.Directory();
        var source = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(source, "file content");
            var writer = new FileSystemArtifactWriter(directory);

            var artifact = await writer.AttachFileAsync("doc", source, "text/plain");

            Assert.Equal(Path.GetFullPath(source), artifact.Path);
            Assert.Equal(new FileInfo(source).Length, artifact.ByteSize);
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            File.Delete(source);
            Cleanup(directory);
        }
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
