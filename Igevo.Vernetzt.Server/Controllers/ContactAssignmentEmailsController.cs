using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Igevo.Vernetzt.Server.Controllers;

[ApiController]
[Route("api/contact-assignment-emails")]
[Route("contact-assignment-emails")]
public class ContactAssignmentEmailsController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<ContactAssignmentEmailsController> _logger;

    public ContactAssignmentEmailsController(
        IUserService userService,
        ILogger<ContactAssignmentEmailsController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Verifies a context-email token for a contact assignment.
    /// Returns an HTML page because the link is opened directly from mailbox clients.
    /// </summary>
    [HttpGet("verify")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicTokenFlow")]
    public async Task<IActionResult> Verify([FromQuery] string token, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _userService.VerifyContactAssignmentEmailAsync(token, cancellationToken);
            var color = result.Success ? "#4caf50" : "#f44336";
            var icon = result.Success ? "&#10003;" : "&#10007;";

            var html = $@"<!DOCTYPE html>
<html lang=""de"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>{result.Title} - Vernetzt</title>
    <style>
        body {{ font-family: Arial, sans-serif; background: #f5f5f5; margin: 0; padding: 40px 20px; color: #333; }}
        .card {{ max-width: 560px; margin: 0 auto; background: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,.1); padding: 36px; text-align: center; }}
        .icon {{ font-size: 48px; color: {color}; margin-bottom: 16px; }}
        h1 {{ color: #0D4A70; font-size: 24px; margin: 0 0 16px; }}
        p {{ color: #666; line-height: 1.6; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""icon"">{icon}</div>
        <h1>{result.Title}</h1>
        <p>{result.Message}</p>
    </div>
</body>
</html>";

            return new ContentResult
            {
                Content = html,
                ContentType = "text/html; charset=utf-8",
                StatusCode = result.Success ? 200 : 400
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying contact assignment email token");
            return new ContentResult
            {
                Content = "<html><body><h2>Fehler</h2><p>Bei der Verifizierung ist ein Fehler aufgetreten.</p></body></html>",
                ContentType = "text/html; charset=utf-8",
                StatusCode = 500
            };
        }
    }
}
