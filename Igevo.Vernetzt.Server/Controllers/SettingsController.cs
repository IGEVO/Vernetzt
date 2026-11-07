using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Settings;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "SystemAdmin")]
public class SettingsController : ControllerBase
{
    private const long MaxLogoSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly IAppSettingsService _appSettingsService;
    private readonly PortalSettingsService _portalSettings;
    private readonly ILogger<SettingsController> _logger;

    // Well-known setting keys
    private const string KeyContactName = "ContactName";
    private const string KeyContactEmail = "ContactEmail";
    private const string KeyPrivacyPolicyUrl = "PrivacyPolicyUrl";

    private static readonly string[] AllKeys = [KeyContactName, KeyContactEmail, KeyPrivacyPolicyUrl];

    public SettingsController(
        IAppSettingsService appSettingsService,
        PortalSettingsService portalSettings,
        ILogger<SettingsController> logger)
    {
        _appSettingsService = appSettingsService;
        _portalSettings = portalSettings;
        _logger = logger;
    }

    /// <summary>
    /// Gets all application settings (SystemAdmin only).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AppSettingsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AppSettingsDto>>> GetSettings(
        CancellationToken cancellationToken = default)
    {
        var values = await _appSettingsService.GetValuesAsync(AllKeys, cancellationToken);

        var dto = new AppSettingsDto
        {
            ContactName = values.GetValueOrDefault(KeyContactName, ""),
            ContactEmail = values.GetValueOrDefault(KeyContactEmail, ""),
            PrivacyPolicyUrl = values.GetValueOrDefault(KeyPrivacyPolicyUrl, "")
        };

        return Ok(ApiResponse<AppSettingsDto>.CreateSuccess(dto));
    }

    /// <summary>
    /// Updates application settings (SystemAdmin only).
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<AppSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AppSettingsDto>>> UpdateSettings(
        [FromBody] AppSettingsDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.CreateError("Ungültige Eingabe.", "VALIDATION_ERROR"));
        }

        var settings = new Dictionary<string, string>
        {
            [KeyContactName] = dto.ContactName,
            [KeyContactEmail] = dto.ContactEmail,
            [KeyPrivacyPolicyUrl] = dto.PrivacyPolicyUrl
        };

        await _appSettingsService.SetValuesAsync(settings, cancellationToken);

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        _logger.LogInformation("Settings updated by user {UserId}", userId);

        return Ok(ApiResponse<AppSettingsDto>.CreateSuccess(dto));
    }

    /// <summary>
    /// Gets the privacy policy URL (public endpoint for onboarding).
    /// </summary>
    [HttpGet("privacy-policy-url")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<string>>> GetPrivacyPolicyUrl(
        CancellationToken cancellationToken = default)
    {
        var url = await _appSettingsService.GetValueAsync(KeyPrivacyPolicyUrl, cancellationToken);
        return Ok(ApiResponse<string>.CreateSuccess(url ?? ""));
    }

    /// <summary>
    /// Gets texts and branding of the public portal (SystemAdmin only).
    /// </summary>
    [HttpGet("portal")]
    [ProducesResponseType(typeof(ApiResponse<PortalSettingsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PortalSettingsDto>>> GetPortalSettings(
        CancellationToken cancellationToken = default) =>
        Ok(ApiResponse<PortalSettingsDto>.CreateSuccess(await _portalSettings.GetAsync(cancellationToken)));

    /// <summary>
    /// Updates texts of the public portal (SystemAdmin only). Rich text is sanitized on save.
    /// </summary>
    [HttpPut("portal")]
    [ProducesResponseType(typeof(ApiResponse<PortalSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PortalSettingsDto>>> UpdatePortalSettings(
        [FromBody] PortalSettingsDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.CreateError("Ungültige Eingabe.", "VALIDATION_ERROR"));
        }

        var error = await _portalSettings.SaveAsync(dto, cancellationToken);
        if (error is not null)
        {
            return BadRequest(ApiResponse<object>.CreateError(error, "VALIDATION_ERROR"));
        }

        _logger.LogInformation("Portal settings updated by user {UserId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        return Ok(ApiResponse<PortalSettingsDto>.CreateSuccess(await _portalSettings.GetAsync(cancellationToken)));
    }

    /// <summary>
    /// Uploads the logo of the public portal (SystemAdmin only).
    /// </summary>
    [HttpPost("portal/logo")]
    [RequestSizeLimit(MaxLogoSizeBytes)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadPortalLogo(IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.CreateError("Keine Datei hochgeladen."));
        }

        await using var stream = file.OpenReadStream();
        var error = await _portalSettings.UploadLogoAsync(stream, file.FileName, cancellationToken);
        return error is null
            ? Ok(ApiResponse<bool>.CreateSuccess(true))
            : BadRequest(ApiResponse<object>.CreateError(error));
    }

    /// <summary>
    /// Removes the logo of the public portal (SystemAdmin only).
    /// </summary>
    [HttpDelete("portal/logo")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeletePortalLogo(CancellationToken cancellationToken = default)
    {
        await _portalSettings.DeleteLogoAsync(cancellationToken);
        return Ok(ApiResponse<bool>.CreateSuccess(true));
    }

    /// <summary>
    /// Gets the current portal logo for the admin preview (SystemAdmin only, never cached).
    /// </summary>
    [HttpGet("portal/logo")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPortalLogo(
        [FromServices] IFileStorageService fileStorage, CancellationToken cancellationToken = default)
    {
        if (!await _portalSettings.HasLogoAsync(cancellationToken) ||
            !await fileStorage.FileExistsAsync(PortalSettingsService.LogoPath, cancellationToken))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-store";
        return File(await fileStorage.DownloadFileAsync(PortalSettingsService.LogoPath, cancellationToken), "image/webp");
    }
}
