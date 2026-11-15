using Igevo.Vernetzt.Core.DTOs.Public;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Igevo.Vernetzt.Server.Controllers;

[ApiController]
[Route("api/public/v1")]
[AllowAnonymous]
[EnableRateLimiting("PublicApi")]
[OutputCache(PolicyName = "PublicApi")]
[ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
[ApiExplorerSettings(GroupName = "public-v1")]
[Produces("application/json")]
public sealed class PublicDirectoryController(
    IPublicDirectoryService publicDirectory,
    VernetztDbContext dbContext,
    IFileStorageService fileStorage,
    PortalSettingsService portalSettings,
    ILogger<PublicDirectoryController> logger) : ControllerBase
{
    public const string ContactRevealRateLimitPolicy = "PublicContactReveal";

    /// <summary>Texts and branding of the portal (title, legal pages, provider, logo).</summary>
    [HttpGet("portal")]
    [OutputCache(PolicyName = "PublicApi", Tags = [PortalSettingsService.CacheTag])]
    public async Task<ActionResult<PublicPortalDto>> GetPortal(CancellationToken cancellationToken = default) =>
        Ok(await portalSettings.GetPublicAsync(cancellationToken));

    /// <summary>
    /// Portal logo. Requests carrying the version from <see cref="PublicPortalDto.LogoUrl"/> may be
    /// cached for good, since a new upload gets a new version.
    /// </summary>
    [HttpGet("portal/logo")]
    [OutputCache(PolicyName = "PublicMedia", Tags = [PortalSettingsService.CacheTag])]
    public async Task<IActionResult> GetPortalLogo([FromQuery] string? v, CancellationToken cancellationToken = default)
    {
        if (!await portalSettings.HasLogoAsync(cancellationToken)) return NotFound();
        var result = await ServeWebpAsync(PortalSettingsService.LogoPath, cancellationToken);
        if (!string.IsNullOrEmpty(v) && result is FileResult)
        {
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
        return result;
    }

    [HttpGet("clubs")]
    [ProducesResponseType(typeof(PublicPagedResult<PublicClubSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicPagedResult<PublicClubSummaryDto>>> GetClubs(
        [FromQuery] string? query = null,
        [FromQuery] string? city = null,
        [FromQuery] Guid? clubTypeId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await publicDirectory.GetClubsAsync(query, city, clubTypeId, page, pageSize, cancellationToken));

    /// <summary>
    /// A random selection of clubs, so that a start page does not always feature the same
    /// (alphabetically first) clubs. Never cached, the selection changes with every request.
    /// </summary>
    [HttpGet("clubs/random")]
    [OutputCache(NoStore = true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(PublicPagedResult<PublicClubSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicPagedResult<PublicClubSummaryDto>>> GetRandomClubs(
        [FromQuery] int count = 8,
        CancellationToken cancellationToken = default) =>
        Ok(await publicDirectory.GetRandomClubsAsync(count, cancellationToken));

    [HttpGet("clubs/{id:guid}")]
    [ProducesResponseType(typeof(PublicClubDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicClubDto>> GetClub(Guid id, CancellationToken cancellationToken = default)
    {
        var club = await publicDirectory.GetClubAsync(id, cancellationToken);
        return club is null ? NotFound() : Ok(club);
    }

    [HttpGet("departments/{id:guid}")]
    [ProducesResponseType(typeof(PublicDepartmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicDepartmentDto>> GetDepartment(Guid id, CancellationToken cancellationToken = default)
    {
        var department = await publicDirectory.GetDepartmentAsync(id, cancellationToken);
        return department is null ? NotFound() : Ok(department);
    }

    [HttpGet("departments")]
    [ProducesResponseType(typeof(PublicPagedResult<PublicDepartmentSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicPagedResult<PublicDepartmentSummaryDto>>> GetDepartments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await publicDirectory.GetDepartmentsAsync(page, pageSize, cancellationToken));

    [HttpGet("club-types")]
    [ProducesResponseType(typeof(IReadOnlyList<PublicClubTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PublicClubTypeDto>>> GetClubTypes(
        CancellationToken cancellationToken = default) =>
        Ok(await publicDirectory.GetClubTypesAsync(cancellationToken));

    [HttpGet("events")]
    [ProducesResponseType(typeof(PublicPagedResult<PublicEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublicPagedResult<PublicEventDto>>> GetEvents(
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] Guid? clubId = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] bool highlightsOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (fromUtc.HasValue && toUtc.HasValue && fromUtc > toUtc)
        {
            return ValidationProblem("fromUtc darf nicht nach toUtc liegen.");
        }

        return Ok(await publicDirectory.GetEventsAsync(
            fromUtc, toUtc, clubId, departmentId, highlightsOnly, page, pageSize, cancellationToken));
    }

    [HttpGet("events/{id:guid}")]
    [ProducesResponseType(typeof(PublicEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicEventDto>> GetEvent(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await publicDirectory.GetEventAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>
    /// Reveals the club's email address. Kept out of <see cref="PublicClubDto"/> so that addresses
    /// cannot be harvested in bulk; strictly rate limited and never cached.
    /// </summary>
    [HttpGet("clubs/{id:guid}/contact-details")]
    [EnableRateLimiting(ContactRevealRateLimitPolicy)]
    [OutputCache(NoStore = true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(PublicContactDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PublicContactDetailsDto>> GetClubContactDetails(
        Guid id, CancellationToken cancellationToken = default) =>
        OkOrNotFound(await publicDirectory.GetClubContactDetailsAsync(id, cancellationToken));

    /// <summary>Reveals the department's email address (see <see cref="GetClubContactDetails"/>).</summary>
    [HttpGet("departments/{id:guid}/contact-details")]
    [EnableRateLimiting(ContactRevealRateLimitPolicy)]
    [OutputCache(NoStore = true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(PublicContactDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PublicContactDetailsDto>> GetDepartmentContactDetails(
        Guid id, CancellationToken cancellationToken = default) =>
        OkOrNotFound(await publicDirectory.GetDepartmentContactDetailsAsync(id, cancellationToken));

    /// <summary>
    /// Reveals the public email address and phone number of a contact person, identified by
    /// <see cref="PublicContactDto.Id"/> (see <see cref="GetClubContactDetails"/>).
    /// </summary>
    [HttpGet("contacts/{id:guid}/contact-details")]
    [EnableRateLimiting(ContactRevealRateLimitPolicy)]
    [OutputCache(NoStore = true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(PublicContactDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PublicContactDetailsDto>> GetContactDetails(
        Guid id, CancellationToken cancellationToken = default) =>
        OkOrNotFound(await publicDirectory.GetContactDetailsAsync(id, cancellationToken));

    [HttpGet("clubs/{id:guid}/logo")]
    [OutputCache(PolicyName = "PublicMedia")]
    public async Task<IActionResult> GetClubLogo(
        Guid id, [FromQuery] string size = "medium", CancellationToken cancellationToken = default)
    {
        var hasAvatar = await dbContext.Clubs.AsNoTracking()
            .Where(c => c.Id == id).Select(c => c.HasAvatar)
            .FirstOrDefaultAsync(cancellationToken);
        return !hasAvatar
            ? NotFound()
            : await ServeWebpAsync($"avatars/clubs/{id}/{AvatarFileName(size)}", cancellationToken);
    }

    [HttpGet("departments/{id:guid}/logo")]
    [OutputCache(PolicyName = "PublicMedia")]
    public async Task<IActionResult> GetDepartmentLogo(
        Guid id, [FromQuery] string size = "medium", CancellationToken cancellationToken = default)
    {
        var owner = await dbContext.Departments.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new
            {
                DepartmentHasAvatar = d.HasAvatar,
                d.ParentDepartmentId,
                ParentHasAvatar = d.ParentDepartment != null && d.ParentDepartment.HasAvatar,
                d.ClubId,
                ClubHasAvatar = d.Club.HasAvatar
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is null) return NotFound();

        // Own logo, then the parent department's (for groups), then the club's.
        var path = owner.DepartmentHasAvatar
            ? $"avatars/departments/{id}/{AvatarFileName(size)}"
            : owner.ParentHasAvatar
                ? $"avatars/departments/{owner.ParentDepartmentId}/{AvatarFileName(size)}"
                : owner.ClubHasAvatar
                    ? $"avatars/clubs/{owner.ClubId}/{AvatarFileName(size)}"
                    : null;
        return path is null ? NotFound() : await ServeWebpAsync(path, cancellationToken);
    }

    [HttpGet("events/{eventId:guid}/media/{mediaId:guid}/file")]
    [OutputCache(PolicyName = "PublicMedia")]
    public async Task<IActionResult> GetEventMedia(
        Guid eventId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var media = await FindPublicMediaAsync(eventId, mediaId, cancellationToken);
        if (media is null) return NotFound();

        // Same rule as the internal endpoint: images inline, documents only as download.
        var downloadName = media.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? null
            : media.FileName;
        return await ServeFileAsync(media.FilePath, media.ContentType, cancellationToken, downloadName);
    }

    [HttpGet("events/{eventId:guid}/media/{mediaId:guid}/preview/{width:int}")]
    [OutputCache(PolicyName = "PublicMedia")]
    public async Task<IActionResult> GetEventMediaPreview(
        Guid eventId, Guid mediaId, int width, CancellationToken cancellationToken = default)
    {
        var media = await FindPublicMediaAsync(eventId, mediaId, cancellationToken);
        if (media is null) return NotFound();
        var widths = media.GetVariantWidths();
        if (widths.Count == 0) return NotFound();
        var selected = widths.FirstOrDefault(w => w >= width, widths[^1]);
        return await ServeFileAsync(EventMedia.GetVariantPath(eventId, mediaId, selected), "image/webp", cancellationToken);
    }

    private ActionResult<PublicContactDetailsDto> OkOrNotFound(PublicContactDetailsDto? details) =>
        details is null ? NotFound() : Ok(details);

    private Task<EventMedia?> FindPublicMediaAsync(Guid eventId, Guid mediaId, CancellationToken cancellationToken) =>
        dbContext.EventMediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mediaId && m.EventId == eventId, cancellationToken);

    private async Task<IActionResult> ServeWebpAsync(string path, CancellationToken cancellationToken) =>
        await ServeFileAsync(path, "image/webp", cancellationToken);

    private async Task<IActionResult> ServeFileAsync(
        string path, string contentType, CancellationToken cancellationToken, string? downloadName = null)
    {
        try
        {
            if (!await fileStorage.FileExistsAsync(path, cancellationToken)) return NotFound();
            var stream = await fileStorage.DownloadFileAsync(path, cancellationToken);
            Response.Headers.XContentTypeOptions = "nosniff";
            return downloadName is null
                ? File(stream, contentType)
                : File(stream, contentType, downloadName);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not serve public media {Path}", path);
            return NotFound();
        }
    }

    private static string AvatarFileName(string size) => size.ToLowerInvariant() switch
    {
        "thumb" => "avatar-thumb.webp",
        "small" => "avatar-small.webp",
        "original" => "avatar-original.webp",
        _ => "avatar-medium.webp"
    };
}
