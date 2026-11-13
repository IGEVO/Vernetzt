using System.Net;
using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Confirmation of a login address change. The link is opened directly from a mailbox, so the result is an HTML page.
/// </summary>
[ApiController]
[Route("api/account-email-change")]
public class AccountEmailChangeController : ControllerBase
{
    private readonly IAccountEmailChangeService _emailChangeService;
    private readonly ILogger<AccountEmailChangeController> _logger;
    private readonly string _baseUrl;

    public AccountEmailChangeController(
        IAccountEmailChangeService emailChangeService,
        IOptions<AppSettings> appSettings,
        ILogger<AccountEmailChangeController> logger)
    {
        _emailChangeService = emailChangeService;
        _logger = logger;
        _baseUrl = appSettings.Value.BaseUrl.TrimEnd('/');
    }

    [HttpGet("confirm")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicTokenFlow")]
    public async Task<IActionResult> Confirm([FromQuery] string token, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _emailChangeService.ConfirmChangeAsync(token, cancellationToken);
            return Page(result.Success, result.Title, result.Message, result.Success ? 200 : 400);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming login address change");
            return Page(false, "Fehler", "Bei der Bestätigung ist ein Fehler aufgetreten. Bitte versuche es später erneut.", 500);
        }
    }

    private ContentResult Page(bool success, string title, string message, int statusCode)
    {
        var color = success ? "#4caf50" : "#f44336";
        var icon = success ? "&#10003;" : "&#10007;";
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeMessage = WebUtility.HtmlEncode(message);
        var loginUrl = $"{_baseUrl}/login";

        var html = $@"<!DOCTYPE html>
<html lang=""de"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>{safeTitle} - Vernetzt</title>
    <style>
        body {{ font-family: Arial, sans-serif; background: #f5f5f5; margin: 0; padding: 40px 20px; color: #333; }}
        .card {{ max-width: 560px; margin: 0 auto; background: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,.1); padding: 36px; text-align: center; }}
        .icon {{ font-size: 48px; color: {color}; margin-bottom: 16px; }}
        h1 {{ color: #0D4A70; font-size: 24px; margin: 0 0 16px; }}
        p {{ color: #666; line-height: 1.6; }}
        a {{ color: #0D4A70; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""icon"">{icon}</div>
        <h1>{safeTitle}</h1>
        <p>{safeMessage}</p>
        <p><a href=""{WebUtility.HtmlEncode(loginUrl)}"">Zu Vernetzt</a></p>
    </div>
</body>
</html>";

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = statusCode
        };
    }
}
