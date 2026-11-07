using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Igevo.Vernetzt.Core.DTOs.CardDav;
using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services.CardDav;

/// <summary>
/// Read-only CardDAV address book: app password management and vCard generation.
/// Contacts and their visibility come from <see cref="IUserService.GetAllContactsAsync"/>, so the
/// address book shows exactly what the user sees in the web app.
/// </summary>
public class CardDavService : ICardDavService
{
    internal const string OrganizationResourcePrefix = "org-";
    private const string CardExtension = ".vcf";
    private const int PhotoMaxEdge = 200;
    private const int PhotoQuality = 75;
    private static readonly TimeSpan AddressBookCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PhotoCacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan LastUsedGranularity = TimeSpan.FromMinutes(15);
    private static readonly CultureInfo SortCulture = CultureInfo.GetCultureInfo("de-DE");
    private static readonly StringComparer NameComparer = StringComparer.Create(SortCulture, ignoreCase: true);

    private readonly VernetztDbContext _context;
    private readonly IUserService _userService;
    private readonly IAvatarService _avatarService;
    private readonly IImageProcessingService _imageProcessing;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CardDavService> _logger;
    private readonly string _baseUrl;

    public CardDavService(
        VernetztDbContext context,
        IUserService userService,
        IAvatarService avatarService,
        IImageProcessingService imageProcessing,
        IAuditLogRepository auditLogRepository,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<CardDavService> logger)
    {
        _context = context;
        _userService = userService;
        _avatarService = avatarService;
        _imageProcessing = imageProcessing;
        _auditLogRepository = auditLogRepository;
        _cache = cache;
        _logger = logger;
        _baseUrl = (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
    }

    // ---------------------------------------------------------------- access management

    public async Task<CardDavStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        var token = await GetActiveTokenAsync(userId, cancellationToken);
        return MapStatus(user, token);
    }

    public async Task<CardDavAccessCreatedDto> CreateAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        var now = DateTime.UtcNow;

        var active = await _context.CardDavTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync(cancellationToken);
        foreach (var existing in active)
        {
            existing.IsRevoked = true;
            existing.RevokedAt = now;
        }

        var rawToken = GenerateSecureToken();
        var token = new CardDavToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = EmailAddressHelper.Sha256(rawToken),
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.CardDavTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLogRepository.LogAsync(
            nameof(CardDavToken),
            token.Id,
            AuditAction.Create,
            userId,
            null,
            active.Count > 0 ? "CardDAV access renewed" : "CardDAV access created",
            cancellationToken);

        return new CardDavAccessCreatedDto
        {
            Password = rawToken,
            Status = MapStatus(user, token)
        };
    }

    public async Task<bool> RevokeAccessAsync(Guid userId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var active = await _context.CardDavTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync(cancellationToken);
        if (active.Count == 0) return false;

        var now = DateTime.UtcNow;
        foreach (var token in active)
        {
            token.IsRevoked = true;
            token.RevokedAt = now;
        }
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLogRepository.LogAsync(
            nameof(CardDavToken),
            active[0].Id,
            AuditAction.Delete,
            userId,
            null,
            reason is null ? "CardDAV access revoked" : $"CardDAV access revoked ({reason})",
            cancellationToken);

        return true;
    }

    public async Task<CardDavStatusDto> UpdateSettingsAsync(Guid userId, UpdateCardDavSettingsDto dto, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new UnauthorizedAccessException("User not found.");

        if (user.CardDavIncludeOrganizations != dto.IncludeOrganizations)
        {
            user.CardDavIncludeOrganizations = dto.IncludeOrganizations;
            await _context.SaveChangesAsync(cancellationToken);
            _cache.Remove(AddressBookCacheKey(userId));
        }

        var token = await GetActiveTokenAsync(userId, cancellationToken);
        return MapStatus(user, token);
    }

    public async Task<Guid?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || password.Length > 256)
        {
            return null;
        }

        // Look up by hash (unique index) first, then compare the email: no difference in timing
        // between an unknown email address and a wrong password.
        var hash = EmailAddressHelper.Sha256(password);
        var token = await _context.CardDavTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && !t.IsRevoked, cancellationToken);

        var normalizedUsername = username.Trim().ToLowerInvariant();
        if (token?.User is null ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token.User.Email),
                Encoding.UTF8.GetBytes(normalizedUsername)))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (token.LastUsedAt is null || now - token.LastUsedAt.Value > LastUsedGranularity)
        {
            token.LastUsedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return token.UserId;
    }

    // ---------------------------------------------------------------- address book

    public async Task<CardDavAddressBook> GetAddressBookAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(AddressBookCacheKey(userId), out CardDavAddressBook? cached) && cached is not null)
        {
            return cached;
        }

        var user = await GetUserAsync(userId, cancellationToken);
        var cards = new List<CardDavCard>();

        var contacts = await _userService.GetAllContactsAsync(userId, cancellationToken);
        foreach (var contact in contacts.Where(HasReachableData))
        {
            var photo = contact.HasAvatar
                ? await GetPhotoAsync("user", contact.Id, () => _avatarService.GetUserAvatarAsync(contact.Id, cancellationToken))
                : null;
            cards.Add(CreateCard(contact.Id.ToString(), BuildContactCard(contact, photo)));
        }

        if (user.CardDavIncludeOrganizations)
        {
            cards.AddRange(await BuildOrganizationCardsAsync(cancellationToken));
        }

        var addressBook = new CardDavAddressBook(ComputeCTag(cards), cards);
        _cache.Set(AddressBookCacheKey(userId), addressBook, AddressBookCacheDuration);
        _logger.LogDebug("Built CardDAV address book for user {UserId} with {Count} cards", userId, cards.Count);
        return addressBook;
    }

    /// <summary>
    /// A contact only makes sense in an address book if it can be reached: email, phone or an office address.
    /// </summary>
    internal static bool HasReachableData(ContactDto contact) =>
        !string.IsNullOrWhiteSpace(contact.Email) ||
        !string.IsNullOrWhiteSpace(contact.PhoneNumber) ||
        contact.Organizations.Any(o => !string.IsNullOrWhiteSpace(o.AssignmentEmail));

    internal static string BuildContactCard(ContactDto contact, byte[]? photo)
    {
        var organizations = contact.Organizations
            .Where(o => o.Type is OrganizationType.Club or OrganizationType.Department)
            .Select(o => new { Org = o, Path = OrganizationPath(o) })
            .OrderBy(o => string.Join(" › ", o.Path), NameComparer)
            .ToList();

        var clubs = organizations
            .Select(o => o.Path[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, NameComparer)
            .ToList();

        // Positions: with only one club the club is already in ORG, so the position stands alone.
        // Otherwise the organization follows in brackets, abbreviated where an abbreviation is set.
        var withPosition = organizations.Where(o => !string.IsNullOrWhiteSpace(o.Org.PositionName)).ToList();
        var titles = clubs.Count <= 1
            ? withPosition.Select(o => o.Org.PositionName!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : withPosition.Select(o => $"{o.Org.PositionName!.Trim()} ({string.Join(" › ", ShortOrganizationPath(o.Org))})").ToList();

        var notes = organizations.Select(o =>
            string.IsNullOrWhiteSpace(o.Org.PositionName)
                ? string.Join(" › ", o.Path)
                : $"{string.Join(" › ", o.Path)} – {o.Org.PositionName}");

        var builder = new VCardBuilder()
            .AddText("UID", contact.Id.ToString())
            .AddText("FN", $"{contact.FirstName} {contact.LastName}".Trim())
            .AddStructured("N", [contact.LastName, contact.FirstName, null, null, null], allowEmpty: true)
            // ORG is a single, hierarchical value; clients show only one. All clubs therefore go into
            // the organization name, separated by commas (escaped by the builder).
            .AddStructured("ORG", [string.Join(", ", clubs)])
            .AddText("TITLE", string.Join(", ", titles))
            .AddText("EMAIL", contact.Email, "TYPE=INTERNET,PREF");

        var officeEmails = organizations
            .Select(o => o.Org.AssignmentEmail)
            .Where(e => !string.IsNullOrWhiteSpace(e) && !string.Equals(e, contact.Email, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var email in officeEmails)
        {
            builder.AddText("EMAIL", email, "TYPE=INTERNET,WORK");
        }

        return builder
            .AddText("TEL", contact.PhoneNumber, "TYPE=VOICE")
            .AddText("NOTE", string.Join("\n", notes))
            .AddList("CATEGORIES", ["Vernetzt", .. clubs])
            .AddPhoto(photo)
            .Build();
    }

    /// <summary>Club › department › group of an assignment.</summary>
    private static List<string> OrganizationPath(ContactOrganizationDto organization)
    {
        if (organization.Type == OrganizationType.Club)
        {
            return [organization.OrganizationName];
        }

        var path = new List<string>();
        if (!string.IsNullOrWhiteSpace(organization.ParentName)) path.Add(organization.ParentName);
        if (!string.IsNullOrWhiteSpace(organization.ParentDepartmentName)) path.Add(organization.ParentDepartmentName);
        path.Add(organization.OrganizationName);
        return path;
    }

    /// <summary>Like <see cref="OrganizationPath"/>, but each level by its abbreviation if one is set.</summary>
    private static List<string> ShortOrganizationPath(ContactOrganizationDto organization)
    {
        static string Label(string? shortName, string name) => string.IsNullOrWhiteSpace(shortName) ? name : shortName.Trim();

        if (organization.Type == OrganizationType.Club)
        {
            return [Label(organization.OrganizationShortName, organization.OrganizationName)];
        }

        var path = new List<string>();
        if (!string.IsNullOrWhiteSpace(organization.ParentName))
            path.Add(Label(organization.ParentShortName, organization.ParentName));
        if (!string.IsNullOrWhiteSpace(organization.ParentDepartmentName))
            path.Add(Label(organization.ParentDepartmentShortName, organization.ParentDepartmentName));
        path.Add(Label(organization.OrganizationShortName, organization.OrganizationName));
        return path;
    }

    private async Task<List<CardDavCard>> BuildOrganizationCardsAsync(CancellationToken cancellationToken)
    {
        var clubs = await _context.Clubs.AsNoTracking().ToListAsync(cancellationToken);
        var departments = await _context.Departments.AsNoTracking().ToListAsync(cancellationToken);
        var clubsById = clubs.ToDictionary(c => c.Id);
        var departmentsById = departments.ToDictionary(d => d.Id);

        var cards = new List<CardDavCard>();
        foreach (var club in clubs)
        {
            var data = OrganizationInheritanceResult.Resolve([OrganizationInheritanceLevel.Own(club)]);
            if (!HasReachableData(data)) continue;

            var photo = club.HasAvatar
                ? await GetPhotoAsync("club", club.Id, () => _avatarService.GetClubAvatarAsync(club.Id, cancellationToken))
                : null;
            cards.Add(CreateCard(OrganizationResourcePrefix + club.Id,
                BuildOrganizationCard(club.Id, [club.Name], "Verein", club.Description, data, photo)));
        }

        foreach (var department in departments)
        {
            if (!clubsById.TryGetValue(department.ClubId, out var club)) continue;
            var parent = department.ParentDepartmentId is { } parentId && departmentsById.TryGetValue(parentId, out var p) ? p : null;

            var data = OrganizationInheritanceResult.Resolve(OrganizationInheritanceLevel.Chain(department, parent, club));
            if (!HasReachableData(data)) continue;

            // Own logo first, then the parent department's, then the club's (as on the public page).
            byte[]? photo = null;
            if (department.HasAvatar)
                photo = await GetPhotoAsync("department", department.Id, () => _avatarService.GetDepartmentAvatarAsync(department.Id, cancellationToken));
            else if (parent?.HasAvatar == true)
                photo = await GetPhotoAsync("department", parent.Id, () => _avatarService.GetDepartmentAvatarAsync(parent.Id, cancellationToken));
            else if (club.HasAvatar)
                photo = await GetPhotoAsync("club", club.Id, () => _avatarService.GetClubAvatarAsync(club.Id, cancellationToken));

            List<string> path = parent is null
                ? [club.Name, department.Name]
                : [club.Name, parent.Name, department.Name];
            cards.Add(CreateCard(OrganizationResourcePrefix + department.Id,
                BuildOrganizationCard(department.Id, path, parent is null ? "Abteilung" : "Gruppe", department.Description, data, photo)));
        }

        return cards;
    }

    private static bool HasReachableData(OrganizationInheritanceResult data) =>
        data.Address is not null || data.Email is not null || data.Website is not null;

    internal static string BuildOrganizationCard(
        Guid id,
        IReadOnlyList<string> path,
        string kind,
        string? description,
        OrganizationInheritanceResult data,
        byte[]? photo)
    {
        // The whole path goes into the organization name (not into ORG units): company cards are listed
        // by the organization name only (iOS/macOS), so otherwise all departments of a club look alike.
        var displayName = string.Join(" – ", path);

        var builder = new VCardBuilder()
            .AddText("UID", id.ToString())
            .AddText("FN", displayName)
            .AddStructured("N", [null, null, null, null, null], allowEmpty: true)
            .AddStructured("ORG", [displayName])
            .AddRaw("X-ABShowAs", "COMPANY")
            .AddText("EMAIL", data.Email?.Email, "TYPE=INTERNET,WORK");

        if (data.Address is { } address)
        {
            // ADR: post office box; extended address; street; locality; region; postal code; country
            builder.AddStructured("ADR", [null, null, address.Street, address.City, null, address.PostalCode, null], "TYPE=WORK");
        }

        builder.AddText("URL", data.Website?.WebsiteUrl);
        foreach (var link in data.SocialMedia?.SocialMediaLinks ?? [])
        {
            builder.AddText("X-SOCIALPROFILE", link.Url, $"TYPE={link.Platform.ToString().ToLowerInvariant()}");
        }

        return builder
            .AddText("NOTE", description)
            .AddList("CATEGORIES", ["Vernetzt", kind])
            .AddPhoto(photo)
            .Build();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<byte[]?> GetPhotoAsync(string kind, Guid id, Func<Task<byte[]?>> loadOriginal)
    {
        var key = $"carddav-photo:{kind}:{id}";
        if (_cache.TryGetValue(key, out byte[]? cached))
        {
            return cached;
        }

        byte[]? jpeg = null;
        try
        {
            var original = await loadOriginal();
            jpeg = original is null ? null : _imageProcessing.ConvertToJpeg(original, PhotoMaxEdge, PhotoQuality);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not load avatar {Kind}/{Id} for CardDAV", kind, id);
        }

        _cache.Set(key, jpeg, PhotoCacheDuration);
        return jpeg;
    }

    private static CardDavCard CreateCard(string id, string content) =>
        new(id + CardExtension, ComputeETag(content), content);

    private static string ComputeETag(string content) =>
        $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..32].ToLowerInvariant()}\"";

    private static string ComputeCTag(IEnumerable<CardDavCard> cards)
    {
        var joined = string.Join('\n', cards
            .OrderBy(c => c.ResourceName, StringComparer.Ordinal)
            .Select(c => $"{c.ResourceName}:{c.ETag}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..32].ToLowerInvariant();
    }

    private static string AddressBookCacheKey(Guid userId) => $"carddav-book:{userId}";

    private async Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
        ?? throw new UnauthorizedAccessException("User not found.");

    private Task<CardDavToken?> GetActiveTokenAsync(Guid userId, CancellationToken cancellationToken) =>
        _context.CardDavTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private CardDavStatusDto MapStatus(User user, CardDavToken? token) => new()
    {
        HasAccess = token is not null,
        CreatedAt = token?.CreatedAt,
        LastUsedAt = token?.LastUsedAt,
        ServerUrl = $"{_baseUrl}/carddav/",
        Username = user.Email,
        IncludeOrganizations = user.CardDavIncludeOrganizations
    };

    private static string GenerateSecureToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
