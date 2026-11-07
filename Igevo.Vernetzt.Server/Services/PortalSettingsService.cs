using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Igevo.Vernetzt.Core.DTOs.Public;
using Igevo.Vernetzt.Core.DTOs.Settings;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Ganss.Xss;
using Microsoft.AspNetCore.OutputCaching;

namespace Igevo.Vernetzt.Server.Services;

/// <summary>
/// Texts and branding of the public portal, stored in the AppSetting key-value store.
/// </summary>
public sealed partial class PortalSettingsService(
    IAppSettingsService appSettings,
    IHtmlSanitizer htmlSanitizer,
    IImageProcessingService imageProcessing,
    IFileStorageService fileStorage,
    IOutputCacheStore outputCache,
    ILogger<PortalSettingsService> logger)
{
    /// <summary>Output cache tag of all public portal responses; evicted on every change.</summary>
    public const string CacheTag = "portal";

    public const string LogoPath = "portal/logo.webp";

    private const string KeyTitle = "Portal.Title";
    private const string KeySubtitle = "Portal.Subtitle";
    private const string KeyImprintMode = "Portal.ImprintMode";
    private const string KeyImprintHtml = "Portal.ImprintHtml";
    private const string KeyImprintUrl = "Portal.ImprintUrl";
    private const string KeyPrivacyMode = "Portal.PrivacyMode";
    private const string KeyPrivacyHtml = "Portal.PrivacyHtml";
    private const string KeyPrivacyUrl = "Portal.PrivacyUrl";
    private const string KeyProviderName = "Portal.ProviderName";
    private const string KeyProviderUrl = "Portal.ProviderUrl";
    private const string KeyLogoVersion = "Portal.LogoVersion";
    private const string KeyNoticeText = "Portal.NoticeText";
    private const string KeyNoticeLevel = "Portal.NoticeLevel";

    private const string NoticeLevelWarning = "warning";

    private static readonly string[] AllKeys =
    [
        KeyTitle, KeySubtitle, KeyImprintMode, KeyImprintHtml, KeyImprintUrl,
        KeyPrivacyMode, KeyPrivacyHtml, KeyPrivacyUrl, KeyProviderName, KeyProviderUrl, KeyLogoVersion,
        KeyNoticeText, KeyNoticeLevel
    ];

    public async Task<PortalSettingsDto> GetAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Settings;

    private async Task<(PortalSettingsDto Settings, string LogoVersion)> LoadAsync(CancellationToken cancellationToken)
    {
        var values = await appSettings.GetValuesAsync(AllKeys, cancellationToken);
        string Get(string key) => values.GetValueOrDefault(key, string.Empty);

        var settings = new PortalSettingsDto
        {
            Title = Get(KeyTitle),
            Subtitle = Get(KeySubtitle),
            Imprint = new PortalLegalPageDto
            {
                Mode = NormalizeMode(Get(KeyImprintMode)),
                Html = Get(KeyImprintHtml),
                Url = Get(KeyImprintUrl)
            },
            Privacy = new PortalLegalPageDto
            {
                Mode = NormalizeMode(Get(KeyPrivacyMode)),
                Html = Get(KeyPrivacyHtml),
                Url = Get(KeyPrivacyUrl)
            },
            ProviderName = Get(KeyProviderName),
            ProviderUrl = Get(KeyProviderUrl),
            NoticeText = Get(KeyNoticeText),
            NoticeLevel = Get(KeyNoticeLevel) == NoticeLevelWarning ? NoticeLevelWarning : "info",
            HasLogo = Get(KeyLogoVersion).Length > 0
        };
        return (settings, Get(KeyLogoVersion));
    }

    /// <summary>
    /// Validates, sanitizes and stores the settings. Returns an error message for invalid input.
    /// </summary>
    public async Task<string?> SaveAsync(PortalSettingsDto dto, CancellationToken cancellationToken = default)
    {
        var error = ValidateLegalPage(dto.Imprint, "Impressum")
                    ?? ValidateLegalPage(dto.Privacy, "Datenschutz")
                    ?? (dto.ProviderUrl.Trim().Length > 0 && !IsHttpUrl(dto.ProviderUrl)
                        ? "Die Adresse des Anbieters muss mit http:// oder https:// beginnen."
                        : null);
        if (error is not null) return error;

        await appSettings.SetValuesAsync(new Dictionary<string, string>
        {
            [KeyTitle] = dto.Title.Trim(),
            [KeySubtitle] = dto.Subtitle.Trim(),
            [KeyImprintMode] = dto.Imprint.Mode,
            [KeyImprintHtml] = SanitizeHtml(dto.Imprint.Html),
            [KeyImprintUrl] = dto.Imprint.Url.Trim(),
            [KeyPrivacyMode] = dto.Privacy.Mode,
            [KeyPrivacyHtml] = SanitizeHtml(dto.Privacy.Html),
            [KeyPrivacyUrl] = dto.Privacy.Url.Trim(),
            [KeyProviderName] = dto.ProviderName.Trim(),
            [KeyProviderUrl] = dto.ProviderUrl.Trim(),
            [KeyNoticeText] = dto.NoticeText.Trim(),
            [KeyNoticeLevel] = dto.NoticeLevel
        }, cancellationToken);

        await outputCache.EvictByTagAsync(CacheTag, cancellationToken);
        return null;
    }

    public async Task<PublicPortalDto> GetPublicAsync(CancellationToken cancellationToken = default)
    {
        var (settings, logoVersion) = await LoadAsync(cancellationToken);

        return new PublicPortalDto(
            NullIfEmpty(settings.Title),
            NullIfEmpty(settings.Subtitle),
            ToPublic(settings.Imprint),
            ToPublic(settings.Privacy),
            settings.ProviderName.Length > 0
                ? new PublicPortalProviderDto(settings.ProviderName, NullIfEmpty(settings.ProviderUrl))
                : null,
            logoVersion.Length == 0 ? null : $"/api/public/v1/portal/logo?v={WebUtility.UrlEncode(logoVersion)}",
            settings.NoticeText.Length > 0 ? new PublicPortalNoticeDto(settings.NoticeLevel, settings.NoticeText) : null);
    }

    /// <summary>Processes and stores the logo. Returns an error message for unusable images.</summary>
    public async Task<string?> UploadLogoAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        var result = await imageProcessing.ProcessPortalLogoAsync(stream, fileName, cancellationToken);
        if (!result.Success) return result.ErrorMessage ?? "Fehler bei der Bildverarbeitung.";

        using var logo = new MemoryStream(result.Images["logo"].Data);
        await fileStorage.UploadFileWithExactPathAsync(logo, LogoPath, cancellationToken);
        await appSettings.SetValueAsync(
            KeyLogoVersion, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await outputCache.EvictByTagAsync(CacheTag, cancellationToken);
        logger.LogInformation("Portal logo updated");
        return null;
    }

    public async Task DeleteLogoAsync(CancellationToken cancellationToken = default)
    {
        await appSettings.SetValueAsync(KeyLogoVersion, string.Empty, cancellationToken);
        await fileStorage.DeleteFileAsync(LogoPath, cancellationToken);
        await outputCache.EvictByTagAsync(CacheTag, cancellationToken);
        logger.LogInformation("Portal logo removed");
    }

    public async Task<bool> HasLogoAsync(CancellationToken cancellationToken = default) =>
        !string.IsNullOrEmpty(await appSettings.GetValueAsync(KeyLogoVersion, cancellationToken));

    private static string? ValidateLegalPage(PortalLegalPageDto page, string label)
    {
        var url = page.Url.Trim();
        if (page.Mode == PortalLegalPageDto.ModeLink && url.Length == 0)
            return $"{label}: Bitte eine Adresse angeben oder auf eigenen Text umstellen.";
        if (url.Length > 0 && !IsHttpUrl(url))
            return $"{label}: Die Adresse muss mit http:// oder https:// beginnen.";
        return null;
    }

    private static PublicPortalLegalPageDto ToPublic(PortalLegalPageDto page) =>
        page.Mode == PortalLegalPageDto.ModeLink
            ? new PublicPortalLegalPageDto(PortalLegalPageDto.ModeLink, null, page.Url)
            : new PublicPortalLegalPageDto(PortalLegalPageDto.ModeText, NullIfEmpty(page.Html), null);

    /// <summary>Sanitizes rich text; markup without visible text (e.g. an empty paragraph) is stored as empty.</summary>
    private string SanitizeHtml(string html)
    {
        var sanitized = htmlSanitizer.Sanitize(html ?? string.Empty).Trim();
        var text = WebUtility.HtmlDecode(TagPattern().Replace(sanitized, string.Empty));
        return string.IsNullOrWhiteSpace(text) ? string.Empty : sanitized;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string NormalizeMode(string mode) =>
        mode == PortalLegalPageDto.ModeLink ? PortalLegalPageDto.ModeLink : PortalLegalPageDto.ModeText;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();
}
