using System.Security.Claims;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for serving static files from storage.
/// Files are protected and require authentication.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileStorageService _fileStorage;
    private readonly Core.Interfaces.Services.IAuthorizationService _authorizationService;
    private readonly ILogger<FilesController> _logger;

    /// <summary>
    /// MIME types for common file extensions.
    /// </summary>
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".webp", "image/webp" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".svg", "image/svg+xml" },
        { ".pdf", "application/pdf" }
    };

    public FilesController(
        IFileStorageService fileStorage,
        Core.Interfaces.Services.IAuthorizationService authorizationService,
        ILogger<FilesController> logger)
    {
        _fileStorage = fileStorage;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    /// <summary>
    /// Serves a file from storage (requires authentication).
    /// </summary>
    /// <param name="path">The file path (can include subdirectories).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file content.</returns>
    /// <response code="200">File found and returned.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Not authorized to access this file.</response>
    /// <response code="404">File not found.</response>
    [HttpGet("{**path}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any, VaryByHeader = "Authorization")] // Cache for 24 hours
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(string path, CancellationToken cancellationToken)
    {
        try
        {
            // Security: Prevent path traversal attacks
            if (string.IsNullOrWhiteSpace(path) || path.Contains("..") || Path.IsPathRooted(path))
            {
                _logger.LogWarning("Invalid file path requested: {Path}", path);
                return NotFound();
            }

            // Get current user
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return Unauthorized();
            }

            // Check authorization based on path
            var authorized = await IsAuthorizedForPath(currentUserId.Value, path, cancellationToken);
            if (!authorized)
            {
                return Forbid();
            }

            // Check if file exists
            if (!await _fileStorage.FileExistsAsync(path, cancellationToken))
            {
                return NotFound();
            }

            // Get the file
            var stream = await _fileStorage.DownloadFileAsync(path, cancellationToken);

            // Determine content type
            var extension = Path.GetExtension(path);
            var contentType = MimeTypes.GetValueOrDefault(extension, "application/octet-stream");

            return File(stream, contentType);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving file: {Path}", path);
            return NotFound();
        }
    }

    /// <summary>
    /// Checks if the user is authorized to access the file at the given path.
    /// </summary>
    private async Task<bool> IsAuthorizedForPath(Guid userId, string path, CancellationToken cancellationToken)
    {
        // Parse the path to determine what entity it belongs to
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            return false;
        }

        // avatars/{users|clubs|departments}/{entityId}/...
        if (segments[0] == "avatars")
        {
            if (segments.Length < 3)
            {
                return false;
            }

            var entityType = segments[1];
            var entityIdStr = segments[2];

            if (!Guid.TryParse(entityIdStr, out var entityId))
            {
                return false;
            }

            return entityType switch
            {
                "users" => true,
                "clubs" => await _authorizationService.CanAccessClubAsync(userId, entityId, cancellationToken),
                "departments" => await _authorizationService.CanAccessDepartmentAsync(userId, entityId, cancellationToken),
                _ => false
            };
        }

        // event-media/... is intentionally not served here: it is delivered through
        // EventsController, which enforces the per-event visibility rules.

        return false;
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
}
