using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Settings;
using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Server.Services;
using Ganss.Xss;
using Microsoft.Extensions.Options;
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
    private const string KeyImprintMode = "ImprintMode";
    private const string KeyImprintUrl = "ImprintUrl";
    private const string KeyImprintHtml = "ImprintHtml";
    private static readonly string[] ImprintKeys = [KeyImprintMode, KeyImprintUrl, KeyImprintHtml];
    private static readonly string[] AllKeys = [KeyContactName, KeyContactEmail, .. PrivacyPolicySettings.Keys, .. ImprintKeys];

    private readonly IHtmlSanitizer _htmlSanitizer;
    private readonly string _baseUrl;

    public SettingsController(
        IAppSettingsService appSettingsService,
        PortalSettingsService portalSettings,
        IHtmlSanitizer htmlSanitizer,
        IOptions<AppSettings> appSettings,
        ILogger<SettingsController> logger)
    {
        _appSettingsService = appSettingsService;
        _portalSettings = portalSettings;
        _htmlSanitizer = htmlSanitizer;
        _baseUrl = appSettings.Value.BaseUrl;
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
            PrivacyPolicyMode = PrivacyPolicySettings.NormalizeMode(values.GetValueOrDefault(PrivacyPolicySettings.KeyMode)),
            PrivacyPolicyUrl = values.GetValueOrDefault(PrivacyPolicySettings.KeyUrl, ""),
            PrivacyPolicyHtml = values.GetValueOrDefault(PrivacyPolicySettings.KeyHtml, ""),
            ImprintMode = PrivacyPolicySettings.NormalizeMode(values.GetValueOrDefault(KeyImprintMode)),
            ImprintUrl = values.GetValueOrDefault(KeyImprintUrl, ""),
            ImprintHtml = values.GetValueOrDefault(KeyImprintHtml, "")
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

        var mode = PrivacyPolicySettings.NormalizeMode(dto.PrivacyPolicyMode);
        var url = dto.PrivacyPolicyUrl.Trim();
        var html = SanitizeHtml(dto.PrivacyPolicyHtml);
        var imprintMode = PrivacyPolicySettings.NormalizeMode(dto.ImprintMode);
        var imprintUrl = dto.ImprintUrl.Trim();
        var imprintHtml = SanitizeHtml(dto.ImprintHtml);
        var error = imprintUrl.Length > 0 && EmailPrivacyNotice.NormalizeUrl(imprintUrl) == null
            ? "Die Adresse des Impressums muss mit http:// oder https:// beginnen."
            : url.Length > 0 && EmailPrivacyNotice.NormalizeUrl(url) == null
            ? "Die Adresse der Datenschutzerklärung muss mit http:// oder https:// beginnen."
            : mode == PrivacyPolicySettings.ModeLink && url.Length == 0
                ? "Bitte die Adresse der Datenschutzerklärung angeben oder auf eigenen Text umstellen."
                : mode == PrivacyPolicySettings.ModeText && html.Length == 0
                    ? "Bitte einen Text für die Datenschutzerklärung eingeben oder auf externen Link umstellen."
                    : null;
        if (error != null)
        {
            return BadRequest(ApiResponse<object>.CreateError(error, "VALIDATION_ERROR"));
        }

        var settings = new Dictionary<string, string>
        {
            [KeyContactName] = dto.ContactName,
            [KeyContactEmail] = dto.ContactEmail,
            [PrivacyPolicySettings.KeyMode] = mode,
            [PrivacyPolicySettings.KeyUrl] = url,
            [PrivacyPolicySettings.KeyHtml] = html,
            [KeyImprintMode] = imprintMode,
            [KeyImprintUrl] = imprintUrl,
            [KeyImprintHtml] = imprintHtml
        };

        await _appSettingsService.SetValuesAsync(settings, cancellationToken);
        dto.PrivacyPolicyMode = mode;
        dto.PrivacyPolicyUrl = url;
        dto.PrivacyPolicyHtml = html;
        dto.ImprintMode = imprintMode;
        dto.ImprintUrl = imprintUrl;
        dto.ImprintHtml = imprintHtml;

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        _logger.LogInformation("Settings updated by user {UserId}", userId);

        return Ok(ApiResponse<AppSettingsDto>.CreateSuccess(dto));
    }

    /// <summary>
    /// Gets the URL of the privacy policy: the external page or the app's own page (public endpoint).
    /// Empty when no privacy policy is configured.
    /// </summary>
    [HttpGet("privacy-policy-url")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<string>>> GetPrivacyPolicyUrl(
        CancellationToken cancellationToken = default)
    {
        var url = await PrivacyPolicySettings.GetEffectiveUrlAsync(_appSettingsService, _baseUrl, cancellationToken);
        return Ok(ApiResponse<string>.CreateSuccess(url ?? ""));
    }

    /// <summary>
    /// Gets the privacy policy for the public page /datenschutz (no sign-in required): own text or external link.
    /// </summary>
    [HttpGet("privacy-policy")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PublicPrivacyPolicyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PublicPrivacyPolicyDto>>> GetPrivacyPolicy(
        CancellationToken cancellationToken = default)
        => Ok(ApiResponse<PublicPrivacyPolicyDto>.CreateSuccess(await GetLegalPageAsync(PrivacyPolicySettings.Keys, cancellationToken)));

    /// <summary>
    /// Gets the imprint for the public page /impressum (no sign-in required): own text or external link.
    /// </summary>
    [HttpGet("imprint")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PublicPrivacyPolicyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PublicPrivacyPolicyDto>>> GetImprint(
        CancellationToken cancellationToken = default)
        => Ok(ApiResponse<PublicPrivacyPolicyDto>.CreateSuccess(await GetLegalPageAsync(ImprintKeys, cancellationToken)));

    /// <summary>Reads a legal page from its keys in the order mode, url, html.</summary>
    private async Task<PublicPrivacyPolicyDto> GetLegalPageAsync(string[] keys, CancellationToken cancellationToken)
    {
        var values = await _appSettingsService.GetValuesAsync(keys, cancellationToken);
        var mode = PrivacyPolicySettings.NormalizeMode(values.GetValueOrDefault(keys[0]));
        return mode == PrivacyPolicySettings.ModeText
            ? new PublicPrivacyPolicyDto { Mode = mode, Html = NullIfEmpty(values.GetValueOrDefault(keys[2])) }
            : new PublicPrivacyPolicyDto { Mode = mode, Url = EmailPrivacyNotice.NormalizeUrl(values.GetValueOrDefault(keys[1])) };
    }

    /// <summary>Sanitizes rich text; markup without visible text (e.g. an empty paragraph) becomes empty.</summary>
    private string SanitizeHtml(string? html)
    {
        var sanitized = _htmlSanitizer.Sanitize(html ?? string.Empty).Trim();
        var text = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(
            sanitized, "<[^>]*>", string.Empty, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
        return string.IsNullOrWhiteSpace(text) ? string.Empty : sanitized;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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
