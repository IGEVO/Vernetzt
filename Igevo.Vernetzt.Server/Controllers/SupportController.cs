using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Settings;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Help and support information for signed-in users.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SupportController : ControllerBase
{
    private readonly IAppSettingsService _appSettingsService;

    public SupportController(IAppSettingsService appSettingsService)
    {
        _appSettingsService = appSettingsService;
    }

    /// <summary>
    /// Gets the contact person configured in the system settings (any signed-in user).
    /// </summary>
    [HttpGet("contact")]
    [ProducesResponseType(typeof(ApiResponse<SupportContactDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupportContactDto>>> GetContact(
        CancellationToken cancellationToken = default)
    {
        var values = await _appSettingsService.GetValuesAsync(["ContactName", "ContactEmail"], cancellationToken);

        return Ok(ApiResponse<SupportContactDto>.CreateSuccess(new SupportContactDto
        {
            Name = values.GetValueOrDefault("ContactName", ""),
            Email = values.GetValueOrDefault("ContactEmail", "")
        }));
    }
}
