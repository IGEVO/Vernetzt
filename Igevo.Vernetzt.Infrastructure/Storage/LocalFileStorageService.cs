using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Storage;

/// <summary>
/// Local file system implementation of IFileStorageService.
/// Designed for easy migration to cloud storage (S3, Azure Blob) later.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _storageRootPath;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(string storageRootPath, ILogger<LocalFileStorageService> logger)
    {
        _storageRootPath = storageRootPath;
        _logger = logger;

        _logger.LogInformation("LocalFileStorageService initialized with root path: {Path}", _storageRootPath);
    }

    /// <summary>
    /// Ensures the storage root directory exists. Called lazily on first write operation.
    /// </summary>
    private void EnsureStorageDirectoryExists()
    {
        if (!Directory.Exists(_storageRootPath))
        {
            Directory.CreateDirectory(_storageRootPath);
            _logger.LogInformation("Created storage root directory: {Path}", _storageRootPath);
        }
    }

    public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, string category, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure root directory exists (lazy creation)
            EnsureStorageDirectoryExists();

            // Create category directory if it doesn't exist
            var categoryPath = Path.Combine(_storageRootPath, category);
            if (!Directory.Exists(categoryPath))
            {
                Directory.CreateDirectory(categoryPath);
            }

            // Generate unique file name to prevent collisions
            var fileExtension = Path.GetExtension(fileName);
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(categoryPath, uniqueFileName);

            // Write file to disk
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                await stream.CopyToAsync(fileStream, cancellationToken);
            }

            // Return relative path
            var relativePath = Path.Combine(category, uniqueFileName);
            _logger.LogInformation("File uploaded successfully: {Path}", relativePath);

            return relativePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file: {FileName}", fileName);
            throw;
        }
    }

    public async Task<string> UploadFileWithExactPathAsync(Stream stream, string exactPath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure root directory exists (lazy creation)
            EnsureStorageDirectoryExists();

            var fullPath = Path.Combine(_storageRootPath, exactPath);
            var directory = Path.GetDirectoryName(fullPath);

            // Create directory if it doesn't exist
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write file to disk
            using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                await stream.CopyToAsync(fileStream, cancellationToken);
            }

            _logger.LogInformation("File uploaded with exact path: {Path}", exactPath);
            return exactPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file with exact path: {Path}", exactPath);
            throw;
        }
    }

    public async Task<Stream> DownloadFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.Combine(_storageRootPath, filePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var memoryStream = new MemoryStream();
            using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
            {
                await fileStream.CopyToAsync(memoryStream, cancellationToken);
            }

            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file: {FilePath}", filePath);
            throw;
        }
    }

    public Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.Combine(_storageRootPath, filePath);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("File deleted: {FilePath}", filePath);
                return Task.FromResult(true);
            }

            _logger.LogWarning("File not found for deletion: {FilePath}", filePath);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file: {FilePath}", filePath);
            throw;
        }
    }

    public Task<bool> DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.Combine(_storageRootPath, directoryPath);

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
                _logger.LogInformation("Directory deleted: {DirectoryPath}", directoryPath);
                return Task.FromResult(true);
            }

            _logger.LogWarning("Directory not found for deletion: {DirectoryPath}", directoryPath);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting directory: {DirectoryPath}", directoryPath);
            throw;
        }
    }

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_storageRootPath, filePath);
        return Task.FromResult(File.Exists(fullPath));
    }

    public bool ValidateFile(string fileName, long fileSize, string[] allowedExtensions, long maxSizeBytes)
    {
        // Check file extension
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
        {
            _logger.LogWarning("Invalid file extension: {Extension}. Allowed: {AllowedExtensions}",
                extension, string.Join(", ", allowedExtensions));
            return false;
        }

        // Check file size
        if (fileSize > maxSizeBytes)
        {
            _logger.LogWarning("File size {FileSize} exceeds maximum {MaxSize}", fileSize, maxSizeBytes);
            return false;
        }

        return true;
    }
}
