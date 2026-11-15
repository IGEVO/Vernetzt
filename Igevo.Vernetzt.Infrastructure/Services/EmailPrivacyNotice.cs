using System.Net;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Link to the privacy policy (see <see cref="PrivacyPolicySettings"/>) for every email Vernetzt sends, so that recipients
/// without an account (invitations, bulk mails, relay messages) can also reach the information (Art. 13/14 GDPR).
/// </summary>
public static class EmailPrivacyNotice
{
    /// <summary>
    /// The configured URL if it is an absolute http(s) URL; otherwise null (no link is rendered).
    /// </summary>
    public static string? NormalizeUrl(string? url)
    {
        var trimmed = url?.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? trimmed
            : null;
    }

    /// <summary>
    /// Paragraph for the footer of system emails; empty without a valid URL.
    /// </summary>
    public static string FooterHtml(string? url)
    {
        var normalized = NormalizeUrl(url);
        if (normalized == null)
        {
            return string.Empty;
        }

        var encoded = WebUtility.HtmlEncode(normalized);
        return $"<p>Informationen zur Verarbeitung deiner Daten findest du in der <a href='{encoded}'>Datenschutzerklärung</a>.</p>";
    }

    /// <summary>
    /// Plain text line for the footer of system emails; empty without a valid URL.
    /// </summary>
    public static string FooterText(string? url)
    {
        var normalized = NormalizeUrl(url);
        return normalized == null ? string.Empty : $"Informationen zur Verarbeitung deiner Daten: {normalized}\n";
    }

    /// <summary>
    /// Line for the info banner of bulk and relay mails; empty without a valid URL.
    /// </summary>
    public static string BannerHtml(string? url)
    {
        var normalized = NormalizeUrl(url);
        if (normalized == null)
        {
            return string.Empty;
        }

        var encoded = WebUtility.HtmlEncode(normalized);
        return $"<br><em style='font-size: 11px; color: #666;'>Wie Vernetzt deine E-Mail-Adresse verarbeitet: <a href='{encoded}' style='color: #0D4A70;'>Datenschutzerklärung</a></em>";
    }

    /// <summary>
    /// Plain text line for the info banner of bulk and relay mails; empty without a valid URL.
    /// </summary>
    public static string BannerText(string? url)
    {
        var normalized = NormalizeUrl(url);
        return normalized == null ? string.Empty : $"Datenschutz: {normalized}\n";
    }
}
