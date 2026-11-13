using Igevo.Vernetzt.Core.DTOs.Public;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Igevo.Vernetzt.Infrastructure.Services;

public sealed class PublicDirectoryService(VernetztDbContext dbContext) : IPublicDirectoryService
{
    public async Task<PublicPagedResult<PublicClubSummaryDto>> GetClubsAsync(
        string? query, string? city, Guid? clubTypeId, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);
        var clubs = dbContext.Clubs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            clubs = clubs.Where(c => EF.Functions.Like(c.Name, pattern) ||
                                     c.Departments.Any(d => EF.Functions.Like(d.Name, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            clubs = clubs.Where(c => c.City == city.Trim());
        }

        if (clubTypeId.HasValue)
        {
            clubs = clubs.Where(c => c.ClubTypeId == clubTypeId.Value);
        }

        var total = await clubs.CountAsync(cancellationToken);
        var items = await clubs
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ClubSummary)
            .ToListAsync(cancellationToken);

        return new PublicPagedResult<PublicClubSummaryDto>(items, total, page, pageSize);
    }

    public async Task<PublicPagedResult<PublicClubSummaryDto>> GetRandomClubsAsync(
        int count, CancellationToken cancellationToken = default)
    {
        (_, count) = NormalizePaging(1, count);
        var clubs = dbContext.Clubs.AsNoTracking();

        var total = await clubs.CountAsync(cancellationToken);
        var items = await clubs
            .OrderBy(_ => EF.Functions.Random())
            .Take(count)
            .Select(ClubSummary)
            .ToListAsync(cancellationToken);

        return new PublicPagedResult<PublicClubSummaryDto>(items, total, 1, count);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Club, PublicClubSummaryDto>> ClubSummary =
        c => new PublicClubSummaryDto(
            c.Id,
            c.Name,
            c.ClubTypeId,
            c.ClubType != null ? c.ClubType.Name : null,
            c.City,
            c.WebsiteUrl,
            c.HasAvatar ? $"/api/public/v1/clubs/{c.Id}/logo?size=medium" : null,
            c.Departments.Count(d => d.ParentDepartmentId == null));

    public async Task<PublicClubDto?> GetClubAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var club = await dbContext.Clubs
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.ClubType)
            .Include(c => c.Departments)
            .Include(c => c.ContactAssignments).ThenInclude(a => a.User)
            .Include(c => c.ContactAssignments).ThenInclude(a => a.ContactPosition)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (club is null) return null;

        return new PublicClubDto(
            club.Id,
            club.Name,
            club.Description,
            club.ClubTypeId,
            club.ClubType?.Name,
            club.Street,
            club.PostalCode,
            club.City,
            club.WebsiteUrl,
            MapSocialMediaLinks(club.SocialMediaLinks),
            !string.IsNullOrWhiteSpace(club.Email),
            club.HasAvatar ? $"/api/public/v1/clubs/{club.Id}/logo?size=original" : null,
            MapContacts(club.ContactAssignments, club.ContactPersonsVisibility),
            // Groups are listed on their department page
            club.Departments.Where(d => d.ParentDepartmentId == null).OrderBy(d => d.Name).Select(d =>
                MapDepartmentSummary(d, club.HasAvatar, club.Departments.Count(g => g.ParentDepartmentId == d.Id))).ToList(),
            club.UpdatedAt);
    }

    public async Task<PublicDepartmentDto?> GetDepartmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var department = await dbContext.Departments
            .AsNoTracking()
            .AsSplitQuery()
            .Include(d => d.Club)
            .Include(d => d.ParentDepartment)
            .Include(d => d.Groups)
            .Include(d => d.ContactAssignments).ThenInclude(a => a.User)
            .Include(d => d.ContactAssignments).ThenInclude(a => a.ContactPosition)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (department is null) return null;

        var parent = department.ParentDepartment;
        // Group > department > club: the first level with a filled block wins.
        var (address, email, website, socialMedia) = OrganizationInheritanceResult.Resolve(
            OrganizationInheritanceLevel.Chain(department, parent, department.Club));
        var hasLogo = department.HasAvatar || parent?.HasAvatar == true || department.Club.HasAvatar;
        var groupLogoFallback = department.HasAvatar || department.Club.HasAvatar;

        return new PublicDepartmentDto(
            department.Id,
            department.Name,
            department.ClubId,
            department.Club.Name,
            parent is not null ? new PublicOrganizationReferenceDto(parent.Id, parent.Name) : null,
            department.Description,
            address?.Street,
            address?.PostalCode,
            address?.City,
            website?.WebsiteUrl,
            MapSocialMediaLinks(socialMedia?.SocialMediaLinks),
            email is not null,
            hasLogo ? $"/api/public/v1/departments/{department.Id}/logo?size=original" : null,
            MapContacts(department.ContactAssignments, department.ContactPersonsVisibility),
            department.Groups.OrderBy(g => g.Name).Select(g => MapDepartmentSummary(g, groupLogoFallback, 0)).ToList(),
            department.UpdatedAt,
            address?.Source,
            email?.Source,
            website?.Source,
            socialMedia?.Source);
    }

    public async Task<PublicPagedResult<PublicDepartmentSummaryDto>> GetDepartmentsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);
        var query = dbContext.Departments.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(d => d.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new PublicDepartmentSummaryDto(
                d.Id, d.Name, d.City, d.WebsiteUrl,
                d.HasAvatar || (d.ParentDepartment != null && d.ParentDepartment.HasAvatar) || d.Club.HasAvatar
                    ? $"/api/public/v1/departments/{d.Id}/logo?size=medium"
                    : null,
                d.Description,
                d.Groups.Count))
            .ToListAsync(cancellationToken);
        return new PublicPagedResult<PublicDepartmentSummaryDto>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<PublicClubTypeDto>> GetClubTypesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ClubTypes.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new PublicClubTypeDto(t.Id, t.Name, t.Description))
            .ToListAsync(cancellationToken);

    public async Task<PublicPagedResult<PublicEventDto>> GetEventsAsync(
        DateTime? fromUtc, DateTime? toUtc, Guid? clubId, Guid? departmentId,
        bool highlightsOnly, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);
        var events = PublicEventsQuery();

        if (fromUtc.HasValue) events = events.Where(e => e.EndUtc >= fromUtc.Value);
        if (toUtc.HasValue) events = events.Where(e => e.StartUtc <= toUtc.Value);
        if (departmentId.HasValue)
        {
            // A department's events include those of its groups
            events = events.Where(e => e.DepartmentId == departmentId.Value ||
                                       (e.Department != null && e.Department.ParentDepartmentId == departmentId.Value));
        }
        if (clubId.HasValue)
        {
            events = events.Where(e => e.ClubId == clubId.Value ||
                                       (e.Department != null && e.Department.ClubId == clubId.Value));
        }
        if (highlightsOnly) events = events.Where(e => e.IsHighlight);

        var total = await events.CountAsync(cancellationToken);
        var entities = await events.OrderBy(e => e.StartUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = entities.Select(MapEvent).ToList();
        return new PublicPagedResult<PublicEventDto>(items, total, page, pageSize);
    }

    public async Task<PublicEventDto?> GetEventAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await PublicEventsQuery().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return entity is null ? null : MapEvent(entity);
    }

    private IQueryable<Event> PublicEventsQuery() => dbContext.Events
        .AsNoTracking()
        .AsSplitQuery()
        .Include(e => e.Club)
        .Include(e => e.Department).ThenInclude(d => d!.Club)
        .Include(e => e.Department).ThenInclude(d => d!.ParentDepartment)
        .Include(e => e.Media.OrderBy(m => m.SortOrder));

    /// <param name="fallbackHasAvatar">Whether a higher level (department of a group, club) has a logo to fall back to.</param>
    private static PublicDepartmentSummaryDto MapDepartmentSummary(Department department, bool fallbackHasAvatar, int groupCount) =>
        new(
            department.Id,
            department.Name,
            department.City,
            department.WebsiteUrl,
            department.HasAvatar || fallbackHasAvatar ? $"/api/public/v1/departments/{department.Id}/logo?size=medium" : null,
            department.Description,
            groupCount);

    private static IReadOnlyList<PublicSocialMediaLinkDto> MapSocialMediaLinks(IEnumerable<SocialMediaLink>? links) =>
        (links ?? []).Select(l => new PublicSocialMediaLinkDto(l.Platform, l.Url)).ToList();

    public async Task<PublicContactDetailsDto?> GetClubContactDetailsAsync(
        Guid clubId, CancellationToken cancellationToken = default)
    {
        var email = await dbContext.Clubs.AsNoTracking()
            .Where(c => c.Id == clubId).Select(c => c.Email)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(email) ? null : new PublicContactDetailsDto(email, null);
    }

    public async Task<PublicContactDetailsDto?> GetDepartmentContactDetailsAsync(
        Guid departmentId, CancellationToken cancellationToken = default)
    {
        var email = await dbContext.Departments.AsNoTracking()
            .Where(d => d.Id == departmentId).Select(d => d.Email)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(email) ? null : new PublicContactDetailsDto(email, null);
    }

    public async Task<PublicContactDetailsDto?> GetContactDetailsAsync(
        Guid contactId, CancellationToken cancellationToken = default)
    {
        var assignment = await dbContext.ContactAssignments
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Club)
            .Include(a => a.Department)
            .FirstOrDefaultAsync(a => a.Id == contactId, cancellationToken);
        if (assignment is null) return null;

        var containerVisibility = assignment.Club?.ContactPersonsVisibility
                                  ?? assignment.Department?.ContactPersonsVisibility;
        if (containerVisibility != VisibilityLevel.Public) return null;

        var (email, phone) = ResolvePublicContactFields(assignment);
        return email is null && phone is null ? null : new PublicContactDetailsDto(email, phone);
    }

    private static IReadOnlyList<PublicContactDto> MapContacts(
        IEnumerable<ContactAssignment> assignments, VisibilityLevel containerVisibility)
    {
        if (containerVisibility != VisibilityLevel.Public) return [];

        return assignments
            .OrderBy(a => a.ContactPosition != null ? a.ContactPosition.Name : string.Empty)
            .ThenBy(a => a.User.LastName)
            .Select(a =>
            {
                var (email, phone) = ResolvePublicContactFields(a);
                return new PublicContactDto(
                    a.Id,
                    a.User.FirstName,
                    a.User.LastName,
                    a.ContactPosition?.Name,
                    email is not null,
                    phone is not null);
            })
            .ToList();
    }

    /// <summary>
    /// Email and phone of a contact person that may be shown publicly. A verified, public context
    /// email wins over the primary email; an unverified one is never used.
    /// </summary>
    private static (string? Email, string? Phone) ResolvePublicContactFields(ContactAssignment assignment)
    {
        var contextEmail = assignment.ContextEmailVerified &&
                           assignment.ContextEmailVisibility == VisibilityLevel.Public
            ? assignment.ContextEmail
            : null;
        var primaryEmail = assignment.User.EmailVisibility == VisibilityLevel.Public ? assignment.User.Email : null;
        var phone = assignment.User.PhoneVisibility == VisibilityLevel.Public ? assignment.User.PhoneNumber : null;
        return (NullIfBlank(contextEmail) ?? NullIfBlank(primaryEmail), NullIfBlank(phone));
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static PublicEventDto MapEvent(Event entity)
    {
        var media = entity.Media.OrderBy(m => m.SortOrder).Select(MapMedia).ToList();
        var image = ResolveImage(entity.Media);
        var card = entity.Media.FirstOrDefault(m => m.HasPreview && m.Usages.HasFlag(EventMediaUsage.Card)) ?? image;
        var header = entity.Media.FirstOrDefault(m => m.HasPreview && m.Usages.HasFlag(EventMediaUsage.Header)) ?? image;

        return new PublicEventDto(
            entity.Id, entity.Title, entity.Description, entity.Location,
            entity.StartUtc, entity.EndUtc, entity.IsAllDay, entity.IsHighlight,
            entity.ClubId, entity.DepartmentId,
            // Full organizer path: club > department > group
            entity.Club?.Name ?? entity.Department?.Club?.Name,
            entity.Department?.Name,
            entity.Department?.ParentDepartment?.Name,
            image is null ? null : MapMedia(image),
            card is null ? null : MapMedia(card),
            header is null ? null : MapMedia(header),
            media,
            entity.UpdatedAt);
    }

    private static EventMedia? ResolveImage(IEnumerable<EventMedia> media)
    {
        var previewable = media.Where(m => m.HasPreview).OrderBy(m => m.SortOrder).ToList();
        return previewable.FirstOrDefault(m => m.Type == EventMediaType.Cover)
               ?? previewable.FirstOrDefault(m => m.Type == EventMediaType.Image)
               ?? previewable.FirstOrDefault();
    }

    private static PublicEventImageDto MapMedia(EventMedia media)
    {
        var baseUrl = $"/api/public/v1/events/{media.EventId}/media/{media.Id}";
        return new PublicEventImageDto(
            media.Id, media.Type, media.ContentType, media.FocusX, media.FocusY,
            media.Width, media.Height, $"{baseUrl}/file",
            media.GetVariantWidths().Select(w => new PublicEventImageVariantDto(w, $"{baseUrl}/preview/{w}")).ToList());
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
}
