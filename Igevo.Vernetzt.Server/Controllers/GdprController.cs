using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Gdpr;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for GDPR compliance features (data export, account deletion).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class GdprController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<GdprController> _logger;

    public GdprController(
        IUserService userService,
        ILogger<GdprController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// Export all personal data for the current user (GDPR Article 20 - Right to Data Portability).
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(ApiResponse<UserDataExport>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportMyData(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var export = await _userService.ExportUserDataAsync(userId, userId, cancellationToken);
            return Ok(ApiResponse<UserDataExport>.CreateSuccess(export));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized data export attempt");
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting user data");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Exportieren der Daten."));
        }
    }

    /// <summary>
    /// Export all personal data as downloadable ZIP archive (export JSON plus media such as the profile picture).
    /// </summary>
    [HttpGet("export/download")]
    [Produces("application/zip")]
    public async Task<IActionResult> DownloadMyData(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var archive = await _userService.ExportUserDataArchiveAsync(userId, userId, cancellationToken);

            var fileName = $"datenexport_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
            return File(archive, "application/zip", fileName);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized data export attempt");
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting user data");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Exportieren der Daten."));
        }
    }

    /// <summary>
    /// Export data for another user (admin/manager only).
    /// </summary>
    [HttpGet("export/{userId}")]
    [ProducesResponseType(typeof(ApiResponse<UserDataExport>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportUserData(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var requestingUserId = GetUserId();
            var export = await _userService.ExportUserDataAsync(requestingUserId, userId, cancellationToken);
            return Ok(ApiResponse<UserDataExport>.CreateSuccess(export));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized data export attempt for user {UserId}", userId);
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "User {UserId} not found for export", userId);
            return NotFound(ApiResponse<object>.CreateError("Benutzer nicht gefunden."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting data for user {UserId}", userId);
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Exportieren der Daten."));
        }
    }

    /// <summary>
    /// Delete the current user's account (GDPR Article 17 - Right to Erasure).
    /// </summary>
    [HttpPost("delete-account")]
    [ProducesResponseType(typeof(ApiResponse<DeleteAccountResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteMyAccount([FromBody] DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var response = await _userService.DeleteAccountAsync(userId, userId, request.ConfirmEmail, request.Reason, cancellationToken);

            if (!response.Success)
            {
                return BadRequest(ApiResponse<DeleteAccountResponse>.CreateError(response.Message));
            }

            return Ok(ApiResponse<DeleteAccountResponse>.CreateSuccess(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting account");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Löschen des Kontos."));
        }
    }

    /// <summary>
    /// Delete another user's account (admin/manager only).
    /// </summary>
    [HttpPost("delete-account/{userId}")]
    [ProducesResponseType(typeof(ApiResponse<DeleteAccountResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteUserAccount(Guid userId, [FromBody] DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var requestingUserId = GetUserId();
            var response = await _userService.DeleteAccountAsync(requestingUserId, userId, request.ConfirmEmail, request.Reason, cancellationToken);

            if (!response.Success)
            {
                // Distinguish between authorization errors and other errors
                if (response.Message.Contains("eigenes Konto") || response.Message.Contains("nicht gefunden"))
                {
                    return Forbid();
                }
                return BadRequest(ApiResponse<DeleteAccountResponse>.CreateError(response.Message));
            }

            return Ok(ApiResponse<DeleteAccountResponse>.CreateSuccess(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting account {UserId}", userId);
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Löschen des Kontos."));
        }
    }

}
