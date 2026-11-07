using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for managing user, club, and department avatars.
/// All endpoints require authentication.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class AvatarsController : ControllerBase
{
    private readonly IAvatarService _avatarService;
    private readonly IImageProcessingService _imageProcessingService;
    private readonly IFileStorageService _fileStorage;
    private readonly Core.Interfaces.Services.IAuthorizationService _authorizationService;
    private readonly ILogger<AvatarsController> _logger;

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public AvatarsController(
        IAvatarService avatarService,
        IImageProcessingService imageProcessingService,
        IFileStorageService fileStorage,
        Core.Interfaces.Services.IAuthorizationService authorizationService,
        ILogger<AvatarsController> logger)
    {
        _avatarService = avatarService;
        _imageProcessingService = imageProcessingService;
        _fileStorage = fileStorage;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    #region User Avatars

    /// <summary>
    /// Uploads an avatar for the current user.
    /// </summary>
    [HttpPost("users/me")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> UploadMyAvatar(IFormFile file, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht autorisiert."));
        }

        return await UploadAvatarInternal(
            file,
            () => _avatarService.UploadUserAvatarAsync(userId.Value, file.OpenReadStream(), file.FileName, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Uploads an avatar for a specific user (requires SystemAdmin or self).
    /// </summary>
    [HttpPost("users/{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> UploadUserAvatar(Guid userId, IFormFile file, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht autorisiert."));
        }

        if (currentUserId != userId && !await _authorizationService.IsSystemAdminAsync(currentUserId.Value, cancellationToken))
        {
            return Forbid();
        }

        return await UploadAvatarInternal(
            file,
            () => _avatarService.UploadUserAvatarAsync(userId, file.OpenReadStream(), file.FileName, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Deletes the avatar for the current user.
    /// </summary>
    [HttpDelete("users/me")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMyAvatar(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht autorisiert."));
        }

        var result = await _avatarService.DeleteUserAvatarAsync(userId.Value, cancellationToken);
        return Ok(ApiResponse<bool>.CreateSuccess(result));
    }

    /// <summary>
    /// Deletes the avatar for a specific user (requires SystemAdmin or self).
    /// </summary>
    [HttpDelete("users/{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteUserAvatar(Guid userId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht autorisiert."));
        }

        if (currentUserId != userId && !await _authorizationService.IsSystemAdminAsync(currentUserId.Value, cancellationToken))
        {
            return Forbid();
        }

        var result = await _avatarService.DeleteUserAvatarAsync(userId, cancellationToken);
        return Ok(ApiResponse<bool>.CreateSuccess(result));
    }

    /// <summary>
    /// Gets the avatar image for the current user.
    /// </summary>
    [HttpGet("users/me")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetMyAvatarImage([FromQuery] string size = "medium", CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        return await GetAvatarImageInternal("avatars/users", userId.Value, size, cancellationToken);
    }

    /// <summary>
    /// Gets the avatar image for a user by ID.
    /// </summary>
    [HttpGet("users/{userId:guid}")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetUserAvatarImage(Guid userId, [FromQuery] string size = "medium", CancellationToken cancellationToken = default)
    {
        return await GetAvatarImageInternal("avatars/users", userId, size, cancellationToken);
    }

    #endregion

    #region Club Avatars

    /// <summary>
    /// Uploads an avatar for a club.
    /// </summary>
    [HttpPost("clubs/{clubId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> UploadClubAvatar(Guid clubId, IFormFile file, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht autorisiert."));
        }

        if (!await _authorizationService.CanEditClubAsync(currentUserId.Value, clubId, cancellationToken))
        {
            return Forbid();
        }

        return await UploadAvatarInternal(
            file,
            () => _avatarService.UploadClubAvatarAsync(clubId, file.OpenReadStream(), file.FileName, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Deletes the avatar for a club.
    /// </summary>
    [HttpDelete("clubs/{clubId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteClubAvatar(Guid clubId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht autorisiert."));
        }

        if (!await _authorizationService.CanEditClubAsync(currentUserId.Value, clubId, cancellationToken))
        {
            return Forbid();
        }

        var result = await _avatarService.DeleteClubAvatarAsync(clubId, cancellationToken);
        return Ok(ApiResponse<bool>.CreateSuccess(result));
    }

    /// <summary>
    /// Gets the avatar image for a club.
    /// </summary>
    [HttpGet("clubs/{clubId:guid}")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetClubAvatarImage(Guid clubId, [FromQuery] string size = "medium", CancellationToken cancellationToken = default)
    {
        return await GetAvatarImageInternal("avatars/clubs", clubId, size, cancellationToken);
    }

    #endregion

    #region Department Avatars

    /// <summary>
    /// Uploads an avatar for a department.
    /// </summary>
    [HttpPost("departments/{departmentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> UploadDepartmentAvatar(Guid departmentId, IFormFile file, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht autorisiert."));
        }

        if (!await _authorizationService.CanEditDepartmentAsync(currentUserId.Value, departmentId, cancellationToken))
        {
            return Forbid();
        }

        return await UploadAvatarInternal(
            file,
            () => _avatarService.UploadDepartmentAvatarAsync(departmentId, file.OpenReadStream(), file.FileName, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Deletes the avatar for a department.
    /// </summary>
    [HttpDelete("departments/{departmentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteDepartmentAvatar(Guid departmentId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
        {
            return Unauthorized(ApiResponse<object>.CreateError("Nicht autorisiert."));
        }

        if (!await _authorizationService.CanEditDepartmentAsync(currentUserId.Value, departmentId, cancellationToken))
        {
            return Forbid();
        }

        var result = await _avatarService.DeleteDepartmentAvatarAsync(departmentId, cancellationToken);
        return Ok(ApiResponse<bool>.CreateSuccess(result));
    }

    /// <summary>
    /// Gets the avatar image for a department.
    /// Falls back to the club's avatar if the department has no own avatar.
    /// </summary>
    [HttpGet("departments/{departmentId:guid}")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetDepartmentAvatarImage(Guid departmentId, [FromQuery] string size = "medium", CancellationToken cancellationToken = default)
    {
        var directory = await _avatarService.GetDepartmentAvatarDirectoryAsync(departmentId, cancellationToken);
        if (directory == null)
        {
            return NotFound();
        }

        var sizeFile = GetSizeFileName(size);
        var path = $"{directory}/{sizeFile}";
        return await ServeAvatarFile(path, cancellationToken);
    }

    #endregion

    #region Private Methods

    private async Task<IActionResult> UploadAvatarInternal(IFormFile file, Func<Task<AvatarUploadResult>> uploadFunc, CancellationToken cancellationToken)
    {
        var validationResult = ValidateFile(file);
        if (validationResult != null)
        {
            return validationResult;
        }

        var result = await uploadFunc();

        if (!result.Success)
        {
            return BadRequest(ApiResponse<object>.CreateError(result.ErrorMessage ?? "Fehler beim Upload."));
        }

        return Ok(ApiResponse<bool>.CreateSuccess(true));
    }

    private IActionResult? ValidateFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.CreateError("Keine Datei hochgeladen."));
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(ApiResponse<object>.CreateError($"Datei ist zu groß. Maximale Größe: {MaxFileSizeBytes / 1024 / 1024} MB."));
        }

        if (!_imageProcessingService.IsSupportedFormat(file.FileName))
        {
            return BadRequest(ApiResponse<object>.CreateError($"Nicht unterstütztes Bildformat. Erlaubt: {string.Join(", ", _imageProcessingService.SupportedInputFormats)}"));
        }

        return null;
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        return null;
    }

    private static string GetSizeFileName(string? size)
    {
        return size?.ToLowerInvariant() switch
        {
            "thumb" => "avatar-thumb.webp",
            "small" => "avatar-small.webp",
            "original" => "avatar-original.webp",
            _ => "avatar-medium.webp"
        };
    }

    private async Task<IActionResult> GetAvatarImageInternal(string basePath, Guid entityId, string size, CancellationToken cancellationToken)
    {
        var sizeFile = GetSizeFileName(size);
        var path = $"{basePath}/{entityId}/{sizeFile}";
        return await ServeAvatarFile(path, cancellationToken);
    }

    private async Task<IActionResult> ServeAvatarFile(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _fileStorage.FileExistsAsync(path, cancellationToken))
            {
                return NotFound();
            }

            var stream = await _fileStorage.DownloadFileAsync(path, cancellationToken);
            return File(stream, "image/webp");
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving avatar file: {Path}", path);
            return NotFound();
        }
    }

    #endregion
}
