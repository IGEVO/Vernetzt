using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for dashboard statistics and overview data.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class StatsController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<StatsController> _logger;

    public StatsController(
        IDashboardService dashboardService,
        ILogger<StatsController> logger)
    {
        _dashboardService = dashboardService;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// Get all data needed for the dashboard: statistics and user's own clubs/departments.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<DashboardDataDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboardData(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var data = await _dashboardService.GetDashboardDataAsync(userId, cancellationToken);
            return Ok(ApiResponse<DashboardDataDto>.CreateSuccess(data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard data");
            return BadRequest(ApiResponse<object>.CreateError("Fehler beim Abrufen der Dashboard-Daten."));
        }
    }
}
