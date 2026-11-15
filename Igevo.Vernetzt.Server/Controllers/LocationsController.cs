using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Locations;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Central list of locations ("Standorte") that clubs, departments and groups can be linked to.
/// Readable by all signed-in users, maintained by system admins and managers.
/// </summary>
[ApiController]
[Route("api/locations")]
[Authorize]
[Produces("application/json")]
public class LocationsController(ILocationService locationService) : ControllerBase
{
    private const string MaintainerRoles = "SystemAdmin,Manager";

    /// <summary>
    /// All locations ordered by name (for selection fields and the recipient filter).
    /// </summary>
    [HttpGet("active")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LocationSummaryDto>>), StatusCodes.Status200OK)]
    public Task<ActionResult> GetActive(CancellationToken cancellationToken = default) =>
        Run(async () => (IReadOnlyList<LocationSummaryDto>)(await locationService.GetAllAsync(cancellationToken))
            .Select(l => new LocationSummaryDto { Id = l.Id, Name = l.Name })
            .ToList());

    /// <summary>
    /// All locations with details and usage counts.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LocationDto>>), StatusCodes.Status200OK)]
    public Task<ActionResult> GetAll(CancellationToken cancellationToken = default) =>
        Run(async () => await locationService.GetAllAsync(cancellationToken));

    [HttpPost]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<LocationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> Create([FromBody] SaveLocationDto dto, CancellationToken cancellationToken = default) =>
        Run(async () => await locationService.CreateAsync(GetUserId(), dto, cancellationToken));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<LocationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<ActionResult> Update(Guid id, [FromBody] SaveLocationDto dto, CancellationToken cancellationToken = default) =>
        Run(async () => await locationService.UpdateAsync(GetUserId(), id, dto, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        Run(async () =>
        {
            await locationService.DeleteAsync(GetUserId(), id, cancellationToken);
            return true;
        });

    private async Task<ActionResult> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(ApiResponse<T>.CreateSuccess(await action()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.CreateError(ex.Message, "VALIDATION_ERROR"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.CreateError("Standort nicht gefunden.", "NOT_FOUND"));
        }
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
