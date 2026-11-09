using Igevo.Vernetzt.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// The storage service must never read, write or delete outside its root directory.
/// </summary>
[TestClass]
public class LocalFileStorageServiceTests
{
    private string _root = null!;
    private string _outsideFile = null!;
    private LocalFileStorageService _storage = null!;

    [TestInitialize]
    public void Setup()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "vernetzt-storage-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(baseDir, "root");
        Directory.CreateDirectory(_root);
        _outsideFile = Path.Combine(baseDir, "secret.txt");
        File.WriteAllText(_outsideFile, "secret");
        _storage = new LocalFileStorageService(_root, NullLogger<LocalFileStorageService>.Instance);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);

    [TestMethod]
    public async Task RoundTrip_InsideRoot_Works()
    {
        await _storage.UploadFileWithExactPathAsync(new MemoryStream([1, 2, 3]), "avatars/users/x/a.webp");

        Assert.IsTrue(await _storage.FileExistsAsync("avatars/users/x/a.webp"));
        await using var stream = await _storage.DownloadFileAsync("avatars/users/x/a.webp");
        Assert.AreEqual(3, stream.Length);
    }

    [TestMethod]
    [DataRow("../secret.txt")]
    [DataRow("avatars/../../secret.txt")]
    public async Task TraversalPaths_AreRejected(string path)
    {
        Assert.IsFalse(await _storage.FileExistsAsync(path));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _storage.DownloadFileAsync(path));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _storage.DeleteFileAsync(path));
        Assert.IsTrue(File.Exists(_outsideFile));
    }

    [TestMethod]
    public async Task RootedPath_IsRejected()
    {
        Assert.IsFalse(await _storage.FileExistsAsync(_outsideFile));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _storage.DownloadFileAsync(_outsideFile));
    }

    [TestMethod]
    public async Task DeleteFile_ReturnsWhetherFileExisted()
    {
        await _storage.UploadFileWithExactPathAsync(new MemoryStream([1]), "docs/a.txt");

        Assert.IsTrue(await _storage.DeleteFileAsync("docs/a.txt"));
        Assert.IsFalse(await _storage.DeleteFileAsync("docs/a.txt"));
    }

    [TestMethod]
    public async Task DeleteDirectory_RemovesContentsAndReportsMissingDirectory()
    {
        await _storage.UploadFileWithExactPathAsync(new MemoryStream([1]), "events/e1/a.txt");

        Assert.IsTrue(await _storage.DeleteDirectoryAsync("events/e1"));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "events", "e1")));
        Assert.IsFalse(await _storage.DeleteDirectoryAsync("events/e1"));
    }

    [TestMethod]
    public async Task TraversalPaths_AreRejectedForWritesAndDirectoryDeletes()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _storage.UploadFileWithExactPathAsync(new MemoryStream([1]), "../evil.txt"));
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _storage.UploadFileAsync(new MemoryStream([1]), "a.txt", "text/plain", "../outside"));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _storage.DeleteDirectoryAsync(".."));
        Assert.IsTrue(File.Exists(_outsideFile));
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "evil.txt")));
    }

    [TestMethod]
    public async Task DownloadMissingFile_Throws()
    {
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => _storage.DownloadFileAsync("missing.txt"));
    }
}
