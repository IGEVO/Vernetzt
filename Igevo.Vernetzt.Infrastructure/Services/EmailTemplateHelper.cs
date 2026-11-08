namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Shared helper for branded email template fragments (styles, logo header).
/// Used by both EmailService and EmailRelayService to ensure visual consistency.
/// </summary>
internal static class EmailTemplateHelper
{
    internal const string Styles = @"
        body {
            font-family: Arial, sans-serif;
            line-height: 1.6;
            color: #333333;
            background-color: #ffffff;
            margin: 0;
            padding: 0;
        }
        .container {
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #ffffff;
        }
        .button-cell {
            background-color: #0D4A70;
            border-radius: 4px;
        }
        .button {
            display: inline-block;
            padding: 12px 24px;
            background-color: #0D4A70;
            color: #ffffff !important;
            text-decoration: none;
            border-radius: 4px;
            font-weight: 600;
        }
        .info-box {
            background-color: #FEF3C7;
            padding: 15px;
            border-radius: 4px;
            margin: 20px 0;
            border-left: 4px solid #F59E0B;
        }
        .alert {
            padding: 15px;
            border-radius: 4px;
            margin: 0 0 20px 0;
            border-left: 4px solid;
        }
        .alert-success { background-color: #d4edda; border-color: #28a745; color: #155724; }
        .alert-error { background-color: #f8d7da; border-color: #dc3545; color: #721c24; }
        .alert-warning { background-color: #fff3cd; border-color: #ff9800; color: #856404; }
        .alert strong { color: inherit; }
        .link-box {
            word-break: break-all;
            overflow-wrap: break-word;
            max-width: 100%;
            font-size: 13px;
        }
        .muted { color: #666666; }
        .divider {
            margin-top: 24px;
            padding-top: 16px;
            border-top: 1px solid #dddddd;
        }
        .original-message {
            margin: 0;
            padding: 12px;
            border: 1px solid #d1d5db;
            border-radius: 4px;
            background-color: #f8fafc;
            color: #111827;
            font-family: Arial, sans-serif;
            white-space: pre-wrap;
        }
        .footer {
            margin-top: 30px;
            padding-top: 20px;
            border-top: 1px solid #dddddd;
            font-size: 12px;
            color: #666666;
        }
        a {
            color: #0D4A70;
        }
        strong {
            color: #0D4A70;
        }
        h2 {
            color: #0D4A70;
            margin-top: 0;
        }
        h3 {
            color: #333333;
            margin-top: 0;
        }

        /* Dark mode: light blue (#90CAF9) is the app's dark-theme primary and stays readable on #2d2d2d. */
        @media (prefers-color-scheme: dark) {
            body { background-color: #1a1a1a !important; color: #e0e0e0 !important; }
            .container { background-color: #2d2d2d !important; color: #e0e0e0 !important; }
            p { color: #e0e0e0 !important; }
            a { color: #90CAF9 !important; }
            strong { color: #90CAF9 !important; }
            h2 { color: #90CAF9 !important; }
            h3 { color: #e0e0e0 !important; }
            .brand-title { color: #90CAF9 !important; }
            .button-cell, .button { background-color: #1A5F8E !important; }
            .button { color: #ffffff !important; }
            .info-box { background-color: #3a2f1a !important; border-color: #F59E0B !important; color: #e0e0e0 !important; }
            .alert-success { background-color: #1b3a24 !important; color: #b7e4c7 !important; }
            .alert-error { background-color: #3d1f22 !important; color: #f5c2c7 !important; }
            .alert-warning { background-color: #3a2f1a !important; color: #ffe08a !important; }
            .alert p, .alert strong { color: inherit !important; }
            .muted { color: #aaaaaa !important; }
            .divider { border-color: #555555 !important; }
            .original-message { background-color: #1f1f1f !important; border-color: #555555 !important; color: #e0e0e0 !important; }
            .footer { border-color: #555555 !important; color: #999999 !important; }
            .footer p { color: #999999 !important; }
        }

        /* Outlook dark mode */
        [data-ogsc] body { background-color: #1a1a1a !important; color: #e0e0e0 !important; }
        [data-ogsc] .container { background-color: #2d2d2d !important; color: #e0e0e0 !important; }
        [data-ogsc] p { color: #e0e0e0 !important; }
        [data-ogsc] a { color: #90CAF9 !important; }
        [data-ogsc] strong { color: #90CAF9 !important; }
        [data-ogsc] h2 { color: #90CAF9 !important; }
        [data-ogsc] h3 { color: #e0e0e0 !important; }
        [data-ogsc] .brand-title { color: #90CAF9 !important; }
        [data-ogsc] .button-cell, [data-ogsc] .button { background-color: #1A5F8E !important; }
        [data-ogsc] .button { color: #ffffff !important; }
        [data-ogsc] .info-box { background-color: #3a2f1a !important; border-color: #F59E0B !important; color: #e0e0e0 !important; }
        [data-ogsc] .alert-success { background-color: #1b3a24 !important; color: #b7e4c7 !important; }
        [data-ogsc] .alert-error { background-color: #3d1f22 !important; color: #f5c2c7 !important; }
        [data-ogsc] .alert-warning { background-color: #3a2f1a !important; color: #ffe08a !important; }
        [data-ogsc] .alert p, [data-ogsc] .alert strong { color: inherit !important; }
        [data-ogsc] .muted { color: #aaaaaa !important; }
        [data-ogsc] .divider { border-color: #555555 !important; }
        [data-ogsc] .original-message { background-color: #1f1f1f !important; border-color: #555555 !important; color: #e0e0e0 !important; }
        [data-ogsc] .footer { border-color: #555555 !important; color: #999999 !important; }
        [data-ogsc] .footer p { color: #999999 !important; }
    ";

    /// <summary>
    /// Returns a call-to-action button. Table-based so Outlook's Word renderer keeps the coloured
    /// background and padding (it ignores both on a plain &lt;a&gt;).
    /// </summary>
    internal static string Button(string url, string label) => $@"
        <table role='presentation' cellspacing='0' cellpadding='0' border='0' style='margin: 20px 0;'>
            <tr>
                <td class='button-cell' bgcolor='#0D4A70' style='border-radius: 4px; background-color: #0D4A70;'>
                    <a href='{url}' class='button' style='display: inline-block; padding: 12px 24px; color: #ffffff; text-decoration: none; font-weight: 600; border-radius: 4px;'>{System.Net.WebUtility.HtmlEncode(label)}</a>
                </td>
            </tr>
        </table>";

    /// <summary>
    /// Returns the branded logo/header HTML for email templates.
    /// </summary>
    /// <param name="baseUrl">Base URL for linking to the logo image. Pass empty string to omit the logo.</param>
    internal static string GetLogoHeader(string baseUrl)
    {
        var logoHtml = string.IsNullOrEmpty(baseUrl)
            ? ""
            : $"<img src='{baseUrl}/images/logo-no-bg.png' alt='Vernetzt' width='48' height='48' style='width: 48px; height: 48px; vertical-align: middle;' />";

        return $@"
        <div style='text-align: center; padding: 20px 0 15px 0; border-bottom: 2px solid #F59E0B; margin-bottom: 20px;'>
            {logoHtml}
            <div class='brand-title' style='font-size: 24px; font-weight: 700; color: #0D4A70; margin-top: 8px;'>Vernetzt</div>
        </div>";
    }

    /// <summary>
    /// Wraps content HTML in the full branded email template (doctype, head, body, styles).
    /// </summary>
    internal static string WrapInTemplate(string baseUrl, string contentHtml)
    {
        return $@"<!DOCTYPE html>
<html lang='de'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <meta name='color-scheme' content='light dark'>
    <meta name='supported-color-schemes' content='light dark'>
    <title>Vernetzt</title>
    <style>
{Styles}
    </style>
</head>
<body>
    <div class='container'>
        {GetLogoHeader(baseUrl)}
        {contentHtml}
    </div>
</body>
</html>";
    }
}
