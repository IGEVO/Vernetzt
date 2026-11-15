using Igevo.Vernetzt.Core.Interfaces.Services;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Where the privacy policy of Vernetzt lives. Admins either link an external page (setting "PrivacyPolicyUrl")
/// or write an own text ("PrivacyPolicyHtml"), which the app publishes without sign-in at <see cref="PagePath"/>.
/// Every place that links the policy (emails, onboarding, invitation check) uses <see cref="GetEffectiveUrlAsync"/>.
/// </summary>
public static class PrivacyPolicySettings
{
    public const string KeyMode = "PrivacyPolicyMode";
    public const string KeyUrl = "PrivacyPolicyUrl";
    public const string KeyHtml = "PrivacyPolicyHtml";

    public const string ModeLink = "link";
    public const string ModeText = "text";

    /// <summary>Public page of the app that shows the own text.</summary>
    public const string PagePath = "/datenschutz";

    public static readonly string[] Keys = [KeyMode, KeyUrl, KeyHtml];

    /// <summary>Unknown or missing mode means link, the behaviour before own texts existed.</summary>
    public static string NormalizeMode(string? mode) => mode == ModeText ? ModeText : ModeLink;

    /// <summary>
    /// The URL under which the policy can be read, or null when it is not configured (no valid link, or an
    /// empty text, or no valid <c>App:BaseUrl</c> to build the link to the own page).
    /// </summary>
    public static string? ResolveUrl(IReadOnlyDictionary<string, string>? values, string? baseUrl)
    {
        values ??= new Dictionary<string, string>();
        if (NormalizeMode(values.GetValueOrDefault(KeyMode)) == ModeLink)
        {
            return EmailPrivacyNotice.NormalizeUrl(values.GetValueOrDefault(KeyUrl));
        }

        return string.IsNullOrWhiteSpace(values.GetValueOrDefault(KeyHtml))
            ? null
            : EmailPrivacyNotice.NormalizeUrl($"{baseUrl?.Trim().TrimEnd('/')}{PagePath}");
    }

    public static async Task<string?> GetEffectiveUrlAsync(
        IAppSettingsService appSettings, string? baseUrl, CancellationToken cancellationToken = default)
        => ResolveUrl(await appSettings.GetValuesAsync(Keys, cancellationToken) ?? [], baseUrl);
}
