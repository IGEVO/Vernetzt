namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Interface for file storage operations.
/// Designed to allow easy migration from local storage to cloud storage (S3, Azure Blob, etc.).
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Upload a file and return the file path.
    /// </summary>
    /// <param name="stream">File stream to upload.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="contentType">MIME type of the file.</param>
    /// <param name="category">Storage category (e.g., "profile-images", "documents").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Relative path where the file was stored.</returns>
    Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, string category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a file as a stream.
    /// </summary>
    /// <param name="filePath">Relative path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File stream.</returns>
    Task<Stream> DownloadFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a file.
    /// </summary>
    /// <param name="filePath">Relative path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deletion was successful.</returns>
    Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a directory and all its contents.
    /// </summary>
    /// <param name="directoryPath">Relative path to the directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deletion was successful.</returns>
    Task<bool> DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a file exists.
    /// </summary>
    /// <param name="filePath">Relative path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if file exists.</returns>
    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload a file with the exact filename specified (no GUID generation).
    /// Used for avatar images where consistent filenames are needed.
    /// </summary>
    /// <param name="stream">File stream to upload.</param>
    /// <param name="exactPath">Exact relative path including filename.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path where the file was stored.</returns>
    Task<string> UploadFileWithExactPathAsync(Stream stream, string exactPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate file type and size.
    /// </summary>
    /// <param name="fileName">File name with extension.</param>
    /// <param name="fileSize">File size in bytes.</param>
    /// <param name="allowedExtensions">Allowed file extensions (e.g., ".jpg", ".pdf").</param>
    /// <param name="maxSizeBytes">Maximum file size in bytes.</param>
    /// <returns>True if validation passes.</returns>
    bool ValidateFile(string fileName, long fileSize, string[] allowedExtensions, long maxSizeBytes);
}
