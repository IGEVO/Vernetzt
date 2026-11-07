using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Holidays;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Central holiday calendar (school holidays, public holidays). Readable by all signed-in users,
/// maintained by system admins and managers. Not part of any iCal export.
/// </summary>
[ApiController]
[Route("api/holidays")]
[Authorize]
[Produces("application/json")]
public class HolidaysController(IHolidayService holidayService) : ControllerBase
{
    private const string MaintainerRoles = "SystemAdmin,Manager";

    /// <summary>
    /// Holidays overlapping the given days (both inclusive, format yyyy-MM-dd).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<HolidayEntryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> GetEntries([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken = default) =>
        Run(async () => await holidayService.GetEntriesAsync(from, to, cancellationToken));

    [HttpPost]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<HolidayEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> CreateEntry([FromBody] SaveHolidayEntryDto dto, CancellationToken cancellationToken = default) =>
        Run(async () => await holidayService.CreateEntryAsync(GetUserId(), dto, cancellationToken));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<HolidayEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<ActionResult> UpdateEntry(Guid id, [FromBody] SaveHolidayEntryDto dto, CancellationToken cancellationToken = default) =>
        Run(async () => await holidayService.UpdateEntryAsync(GetUserId(), id, dto, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<ActionResult> DeleteEntry(Guid id, CancellationToken cancellationToken = default) =>
        Run(async () =>
        {
            await holidayService.DeleteEntryAsync(GetUserId(), id, cancellationToken);
            return true;
        });

    [HttpGet("sources")]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<HolidaySourceDto>>), StatusCodes.Status200OK)]
    public Task<ActionResult> GetSources(CancellationToken cancellationToken = default) =>
        Run(async () => await holidayService.GetSourcesAsync(cancellationToken));

    /// <summary>
    /// Adds an iCal subscription and synchronizes it right away; a failed download is reported in
    /// <c>lastSyncError</c> of the result.
    /// </summary>
    [HttpPost("sources")]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<HolidaySourceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> CreateSource([FromBody] SaveHolidaySourceDto dto, CancellationToken cancellationToken = default) =>
        Run(async () => await holidayService.CreateSourceAsync(GetUserId(), dto, cancellationToken));

    [HttpPut("sources/{id:guid}")]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<HolidaySourceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<ActionResult> UpdateSource(Guid id, [FromBody] SaveHolidaySourceDto dto, CancellationToken cancellationToken = default) =>
        Run(async () => await holidayService.UpdateSourceAsync(GetUserId(), id, dto, cancellationToken));

    [HttpDelete("sources/{id:guid}")]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<ActionResult> DeleteSource(Guid id, CancellationToken cancellationToken = default) =>
        Run(async () =>
        {
            await holidayService.DeleteSourceAsync(GetUserId(), id, cancellationToken);
            return true;
        });

    [HttpPost("sources/{id:guid}/sync")]
    [Authorize(Roles = MaintainerRoles)]
    [ProducesResponseType(typeof(ApiResponse<HolidaySourceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<ActionResult> SyncSource(Guid id, CancellationToken cancellationToken = default) =>
        Run(async () => await holidayService.SyncSourceAsync(id, cancellationToken));

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
            return NotFound(ApiResponse<object>.CreateError("Eintrag nicht gefunden.", "NOT_FOUND"));
        }
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
