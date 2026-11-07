using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service for managing user, club, and department avatars.
/// </summary>
public class AvatarService : IAvatarService
{
    private readonly IFileStorageService _fileStorage;
    private readonly IImageProcessingService _imageProcessing;
    private readonly IUserRepository _userRepository;
    private readonly IClubRepository _clubRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly ILogger<AvatarService> _logger;

    private const string UsersAvatarPath = "avatars/users";
    private const string ClubsAvatarPath = "avatars/clubs";
    private const string DepartmentsAvatarPath = "avatars/departments";

    public AvatarService(
        IFileStorageService fileStorage,
        IImageProcessingService imageProcessing,
        IUserRepository userRepository,
        IClubRepository clubRepository,
        IDepartmentRepository departmentRepository,
        ILogger<AvatarService> logger)
    {
        _fileStorage = fileStorage;
        _imageProcessing = imageProcessing;
        _userRepository = userRepository;
        _clubRepository = clubRepository;
        _departmentRepository = departmentRepository;
        _logger = logger;
    }

    public async Task<AvatarUploadResult> UploadUserAvatarAsync(Guid userId, Stream imageStream, string fileName, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return new AvatarUploadResult { Success = false, ErrorMessage = "Benutzer nicht gefunden." };
        }

        return await UploadAvatarAsync(UsersAvatarPath, userId, cancellationToken, async () =>
        {
            user.HasAvatar = true;
            await _userRepository.UpdateAsync(user, cancellationToken);
        }, imageStream, fileName);
    }

    public async Task<AvatarUploadResult> UploadClubAvatarAsync(Guid clubId, Stream imageStream, string fileName, CancellationToken cancellationToken = default)
    {
        var club = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
        if (club == null)
        {
            return new AvatarUploadResult { Success = false, ErrorMessage = "Verein nicht gefunden." };
        }

        return await UploadAvatarAsync(ClubsAvatarPath, clubId, cancellationToken, async () =>
        {
            club.HasAvatar = true;
            await _clubRepository.UpdateAsync(club, cancellationToken);
        }, imageStream, fileName);
    }

    public async Task<AvatarUploadResult> UploadDepartmentAvatarAsync(Guid departmentId, Stream imageStream, string fileName, CancellationToken cancellationToken = default)
    {
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
        if (department == null)
        {
            return new AvatarUploadResult { Success = false, ErrorMessage = "Abteilung oder Gruppe nicht gefunden." };
        }

        return await UploadAvatarAsync(DepartmentsAvatarPath, departmentId, cancellationToken, async () =>
        {
            department.HasAvatar = true;
            await _departmentRepository.UpdateAsync(department, cancellationToken);
        }, imageStream, fileName);
    }

    private async Task<AvatarUploadResult> UploadAvatarAsync(string basePath, Guid entityId, CancellationToken cancellationToken, Func<Task> updateHasAvatarAsync, Stream imageStream, string fileName)
    {
        try
        {
            if (!_imageProcessing.IsSupportedFormat(fileName))
            {
                return new AvatarUploadResult
                {
                    Success = false,
                    ErrorMessage = $"Nicht unterstütztes Bildformat. Erlaubt: {string.Join(", ", _imageProcessing.SupportedInputFormats)}"
                };
            }

            var processingResult = await _imageProcessing.ProcessAvatarAsync(imageStream, fileName, cancellationToken);
            if (!processingResult.Success)
            {
                return new AvatarUploadResult { Success = false, ErrorMessage = processingResult.ErrorMessage };
            }

            var entityPath = $"{basePath}/{entityId}";
            await _fileStorage.DeleteDirectoryAsync(entityPath, cancellationToken);

            foreach (var (sizeName, image) in processingResult.Images)
            {
                using var stream = new MemoryStream(image.Data);
                var exactPath = $"{entityPath}/avatar-{sizeName}.{image.Extension}";
                await _fileStorage.UploadFileWithExactPathAsync(stream, exactPath, cancellationToken);
            }

            await updateHasAvatarAsync();
            _logger.LogInformation("Avatar uploaded successfully for {BasePath}/{EntityId}", basePath, entityId);

            return new AvatarUploadResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading avatar for {BasePath}/{EntityId}", basePath, entityId);
            return new AvatarUploadResult { Success = false, ErrorMessage = "Fehler beim Hochladen des Avatars." };
        }
    }

    public Task<byte[]?> GetUserAvatarAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ReadOriginalAvatarAsync(UsersAvatarPath, userId, cancellationToken);

    public Task<byte[]?> GetClubAvatarAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        ReadOriginalAvatarAsync(ClubsAvatarPath, clubId, cancellationToken);

    public Task<byte[]?> GetDepartmentAvatarAsync(Guid departmentId, CancellationToken cancellationToken = default) =>
        ReadOriginalAvatarAsync(DepartmentsAvatarPath, departmentId, cancellationToken);

    private async Task<byte[]?> ReadOriginalAvatarAsync(string basePath, Guid entityId, CancellationToken cancellationToken)
    {
        var path = $"{basePath}/{entityId}/avatar-original.webp";
        if (!await _fileStorage.FileExistsAsync(path, cancellationToken))
        {
            return null;
        }

        await using var stream = await _fileStorage.DownloadFileAsync(path, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public async Task<bool> DeleteUserAvatarAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var path = $"{UsersAvatarPath}/{userId}";
        var result = await _fileStorage.DeleteDirectoryAsync(path, cancellationToken);

        if (result)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user != null)
            {
                user.HasAvatar = false;
                await _userRepository.UpdateAsync(user, cancellationToken);
            }
            _logger.LogInformation("Deleted avatar for user {UserId}", userId);
        }
        return result;
    }

    public async Task<bool> DeleteClubAvatarAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        var path = $"{ClubsAvatarPath}/{clubId}";
        var result = await _fileStorage.DeleteDirectoryAsync(path, cancellationToken);

        if (result)
        {
            var club = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
            if (club != null)
            {
                club.HasAvatar = false;
                await _clubRepository.UpdateAsync(club, cancellationToken);
            }
            _logger.LogInformation("Deleted avatar for club {ClubId}", clubId);
        }
        return result;
    }

    public async Task<bool> DeleteDepartmentAvatarAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        var path = $"{DepartmentsAvatarPath}/{departmentId}";
        var result = await _fileStorage.DeleteDirectoryAsync(path, cancellationToken);

        if (result)
        {
            var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
            if (department != null)
            {
                department.HasAvatar = false;
                await _departmentRepository.UpdateAsync(department, cancellationToken);
            }
            _logger.LogInformation("Deleted avatar for department {DepartmentId}", departmentId);
        }
        return result;
    }

    public async Task<string?> GetDepartmentAvatarDirectoryAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
        if (department == null)
        {
            return null;
        }

        if (department.HasAvatar)
        {
            return $"{DepartmentsAvatarPath}/{departmentId}";
        }

        // Fallback to club avatar
        var club = await _clubRepository.GetByIdAsync(department.ClubId, cancellationToken);
        if (club?.HasAvatar == true)
        {
            return $"{ClubsAvatarPath}/{department.ClubId}";
        }

        return null;
    }
}
