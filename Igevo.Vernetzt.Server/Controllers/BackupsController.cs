using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.DatabaseBackups;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Database backups (SystemAdmin only). Backup files are not downloadable through the API;
/// they are picked up from the backup volume by the host's backup tooling.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "SystemAdmin")]
public class BackupsController : ControllerBase
{
    private readonly IDatabaseBackupService _backupService;
    private readonly ILogger<BackupsController> _logger;

    public BackupsController(IDatabaseBackupService backupService, ILogger<BackupsController> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    /// <summary>
    /// Backup configuration, last failure and existing backups.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<BackupStatusDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<BackupStatusDto>> GetStatus() =>
        Ok(ApiResponse<BackupStatusDto>.CreateSuccess(_backupService.GetStatus()));

    /// <summary>
    /// Creates a backup now (e.g. before an update).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<BackupFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<BackupFileDto>>> CreateBackup(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _logger.LogInformation("Manual database backup requested by user {UserId}", userId);

        try
        {
            var backup = await _backupService.CreateBackupAsync(BackupTrigger.Manual, cancellationToken);
            return Ok(ApiResponse<BackupFileDto>.CreateSuccess(backup, "Sicherung wurde erstellt."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.CreateError("Die Sicherung ist fehlgeschlagen.", "BACKUP_FAILED"));
        }
    }
}
