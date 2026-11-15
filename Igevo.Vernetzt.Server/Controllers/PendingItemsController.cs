using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Admin;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Administration overview of open invitations, pending assignment consents and unconfirmed role/office addresses.
/// Reminders use the existing endpoints (consent reminder, context-email verification resend).
/// </summary>
[ApiController]
[Route("api/pending-items")]
[Authorize(Roles = "SystemAdmin,Manager")]
[Produces("application/json")]
public class PendingItemsController(
    IPendingItemsService pendingItemsService,
    ILogger<PendingItemsController> logger) : ControllerBase
{
    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// Gets all pending invitations, assignment consents and role/office address confirmations.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PendingItemsOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PendingItemsOverviewDto>>> GetOverview(CancellationToken cancellationToken = default)
    {
        try
        {
            var overview = await pendingItemsService.GetOverviewAsync(GetUserId(), cancellationToken);
            return Ok(ApiResponse<PendingItemsOverviewDto>.CreateSuccess(overview));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized attempt to view pending items");
            return StatusCode(403, ApiResponse<object>.CreateError("Nicht autorisiert.", "FORBIDDEN"));
        }
    }

    /// <summary>
    /// Revokes an open invitation; the invitation link stops working immediately.
    /// </summary>
    [HttpDelete("invitations/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var revoked = await pendingItemsService.RevokeInvitationAsync(GetUserId(), id, cancellationToken);
            return revoked
                ? Ok(ApiResponse<object>.CreateSuccess(new { message = "Einladung wurde widerrufen." }))
                : NotFound(ApiResponse<object>.CreateError("Einladung nicht gefunden oder nicht mehr offen.", "NOT_FOUND"));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized attempt to revoke invitation {InvitationId}", id);
            return StatusCode(403, ApiResponse<object>.CreateError("Nicht autorisiert.", "FORBIDDEN"));
        }
    }
}
