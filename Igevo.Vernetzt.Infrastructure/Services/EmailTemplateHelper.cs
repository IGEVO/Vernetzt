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
        .button {
            display: inline-block;
            padding: 12px 24px;
            background-color: #0D4A70 !important;
            color: #ffffff !important;
            text-decoration: none;
            border-radius: 4px;
            margin: 20px 0;
            font-weight: 600;
        }
        .button:hover {
            background-color: #083752 !important;
        }
        .info-box {
            background-color: #FEF3C7;
            padding: 15px;
            border-radius: 4px;
            margin: 20px 0;
            border-left: 4px solid #F59E0B;
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

        /* Dark Mode Support */
        @media (prefers-color-scheme: dark) {
            body {
                background-color: #1a1a1a !important;
                color: #e0e0e0 !important;
            }
            .container {
                background-color: #2d2d2d !important;
                color: #e0e0e0 !important;
            }
            .button {
                background-color: #1A5F8E !important;
                color: #ffffff !important;
            }
            .button:hover {
                background-color: #0D4A70 !important;
            }
            .info-box {
                background-color: #3a2f1a !important;
                border-color: #F59E0B !important;
                color: #e0e0e0 !important;
            }
            .footer {
                border-color: #555555 !important;
                color: #999999 !important;
            }
            a {
                color: #1A5F8E !important;
            }
            strong {
                color: #F59E0B !important;
            }
            h2 {
                color: #1A5F8E !important;
            }
            h3 {
                color: #e0e0e0 !important;
            }
            p {
                color: #e0e0e0 !important;
            }
        }

        /* Outlook Dark Mode Support */
        [data-ogsc] body { background-color: #1a1a1a !important; color: #e0e0e0 !important; }
        [data-ogsc] .container { background-color: #2d2d2d !important; color: #e0e0e0 !important; }
        [data-ogsc] .button { background-color: #1A5F8E !important; }
        [data-ogsc] .info-box { background-color: #3a2f1a !important; border-color: #F59E0B !important; }
        [data-ogsc] .footer { border-color: #555555 !important; color: #999999 !important; }
        [data-ogsc] a { color: #1A5F8E !important; }
        [data-ogsc] strong { color: #F59E0B !important; }
        [data-ogsc] h2 { color: #1A5F8E !important; }
        [data-ogsc] h3 { color: #e0e0e0 !important; }
        [data-ogsc] p { color: #e0e0e0 !important; }
    ";

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
            <div style='font-size: 24px; font-weight: 700; color: #0D4A70; margin-top: 8px;'>Vernetzt</div>
            <div style='font-size: 12px; color: #666666; letter-spacing: 0.1em; text-transform: uppercase;'>Vereinsverzeichnis</div>
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
