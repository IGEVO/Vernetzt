using System.Security.Cryptography;
using System.Text;
using Igevo.Vernetzt.Core.DTOs.Events;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service for calendar feed token management and ICS feed generation.
/// </summary>
public class CalendarFeedService : ICalendarFeedService
{
    private readonly ICalendarFeedTokenRepository _tokenRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IAuthorizationService _authorizationService;
    private readonly IUserRepository _userRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IDataProtector _tokenProtector;
    private readonly string _baseUrl;
    private readonly TimeZoneInfo _berlinTimeZone;
    private readonly ILogger<CalendarFeedService> _logger;

    public CalendarFeedService(
        ICalendarFeedTokenRepository tokenRepository,
        IEventRepository eventRepository,
        IAuthorizationService authorizationService,
        IUserRepository userRepository,
        IAuditLogRepository auditLogRepository,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<CalendarFeedService> logger)
    {
        _tokenRepository = tokenRepository;
        _eventRepository = eventRepository;
        _authorizationService = authorizationService;
        _userRepository = userRepository;
        _auditLogRepository = auditLogRepository;
        _logger = logger;
        _tokenProtector = dataProtectionProvider.CreateProtector("Igevo.Vernetzt.CalendarFeedToken.v1");

        _baseUrl = (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
        _berlinTimeZone = ResolveBerlinTimeZone();
    }

    public async Task<CalendarFeedTokenCreatedDto> CreateTokenAsync(Guid userId, CreateCalendarFeedTokenDto dto, CancellationToken cancellationToken = default)
    {
        await ValidateCreatePermissionsAsync(userId, dto, cancellationToken);

        var rawToken = GenerateSecureToken();
        var tokenEntity = new CalendarFeedToken
        {
            Scope = dto.Scope,
            ClubId = dto.ClubId,
            DepartmentId = dto.DepartmentId,
            IncludeDepartments = dto.Scope == CalendarFeedScope.Club && dto.IncludeDepartments,
            HighlightsOnly = dto.Scope == CalendarFeedScope.Global && dto.HighlightsOnly,
            IsRevoked = false,
            TokenHash = EmailAddressHelper.Sha256(rawToken),
            ProtectedToken = ProtectToken(rawToken)
        };

        var created = await _tokenRepository.AddAsync(tokenEntity, cancellationToken);

        await _auditLogRepository.LogAsync(
            nameof(CalendarFeedToken),
            created.Id,
            AuditAction.Create,
            userId,
            null,
            $"Created calendar feed token ({created.Scope})",
            cancellationToken);

        return new CalendarFeedTokenCreatedDto
        {
            Token = MapToken(created),
            FeedUrl = BuildFeedUrl(rawToken)
        };
    }

    public async Task<IReadOnlyList<CalendarFeedTokenDto>> GetTokensAsync(
        Guid userId,
        Guid? clubId = null,
        Guid? departmentId = null,
        bool globalOnly = false,
        CancellationToken cancellationToken = default)
    {
        List<CalendarFeedToken> tokens;
        bool canManage;

        if (globalOnly)
        {
            canManage = await IsManagerOrAdminAsync(userId, cancellationToken);
            tokens = (await _tokenRepository.GetGlobalAsync(cancellationToken)).ToList();
        }
        else if (clubId.HasValue)
        {
            canManage = await _authorizationService.CanEditClubAsync(userId, clubId.Value, cancellationToken);
            tokens = (await _tokenRepository.GetByClubAsync(clubId.Value, cancellationToken)).ToList();
        }
        else if (departmentId.HasValue)
        {
            canManage = await _authorizationService.CanEditDepartmentAsync(userId, departmentId.Value, cancellationToken);
            tokens = (await _tokenRepository.GetByDepartmentAsync(departmentId.Value, cancellationToken)).ToList();
        }
        else
        {
            throw new InvalidOperationException("One scope selector is required: clubId, departmentId or globalOnly.");
        }

        // Read-only users only see active links; revoked tokens stay with the managers.
        if (!canManage)
        {
            tokens = tokens
                .Where(t => !t.IsRevoked)
                .ToList();
        }

        return tokens.Select(MapToken).ToList();
    }

    public async Task<CalendarFeedTokenCreatedDto?> RotateTokenAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken = default)
    {
        var existing = await _tokenRepository.GetByIdAsync(tokenId, cancellationToken);
        if (existing == null)
        {
            return null;
        }

        await EnsureTokenEditPermissionAsync(userId, existing, cancellationToken);

        existing.IsRevoked = true;
        existing.RevokedAt = DateTime.UtcNow;
        await _tokenRepository.UpdateAsync(existing, cancellationToken);

        var rawToken = GenerateSecureToken();
        var newToken = new CalendarFeedToken
        {
            Scope = existing.Scope,
            ClubId = existing.ClubId,
            DepartmentId = existing.DepartmentId,
            IncludeDepartments = existing.IncludeDepartments,
            HighlightsOnly = existing.HighlightsOnly,
            IsRevoked = false,
            TokenHash = EmailAddressHelper.Sha256(rawToken),
            ProtectedToken = ProtectToken(rawToken)
        };

        var created = await _tokenRepository.AddAsync(newToken, cancellationToken);

        await _auditLogRepository.LogAsync(
            nameof(CalendarFeedToken),
            created.Id,
            AuditAction.Update,
            userId,
            $"Rotated old token: {tokenId}",
            $"New token: {created.Id}",
            cancellationToken);

        return new CalendarFeedTokenCreatedDto
        {
            Token = MapToken(created),
            FeedUrl = BuildFeedUrl(rawToken)
        };
    }

    public async Task<bool> RevokeTokenAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken = default)
    {
        var existing = await _tokenRepository.GetByIdAsync(tokenId, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        await EnsureTokenEditPermissionAsync(userId, existing, cancellationToken);

        existing.IsRevoked = true;
        existing.RevokedAt = DateTime.UtcNow;
        await _tokenRepository.UpdateAsync(existing, cancellationToken);

        await _auditLogRepository.LogAsync(
            nameof(CalendarFeedToken),
            existing.Id,
            AuditAction.Delete,
            userId,
            "Token active",
            "Token revoked",
            cancellationToken);

        return true;
    }

    public async Task<CalendarFeedContentDto?> GetFeedByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = EmailAddressHelper.Sha256(token.Trim());
        var tokenEntity = await _tokenRepository.GetActiveByTokenHashAsync(hash, cancellationToken);
        if (tokenEntity == null)
        {
            return null;
        }

        var events = await ResolveEventsForTokenAsync(tokenEntity, cancellationToken);
        var content = BuildIcs(events, tokenEntity);

        return new CalendarFeedContentDto
        {
            FileName = BuildFileName(tokenEntity),
            Content = content
        };
    }

    private async Task<IEnumerable<Event>> ResolveEventsForTokenAsync(CalendarFeedToken token, CancellationToken cancellationToken)
    {
        IEnumerable<Event> source;

        if (token.Scope == CalendarFeedScope.Club && token.ClubId.HasValue)
        {
            source = await _eventRepository.GetByClubAsync(
                clubId: token.ClubId.Value,
                includeDepartments: token.IncludeDepartments,
                fromUtc: null,
                toUtc: null,
                cancellationToken: cancellationToken);
        }
        else if (token.Scope == CalendarFeedScope.Department && token.DepartmentId.HasValue)
        {
            source = await _eventRepository.GetByDepartmentAsync(
                departmentId: token.DepartmentId.Value,
                fromUtc: null,
                toUtc: null,
                cancellationToken: cancellationToken);
        }
        else
        {
            source = await _eventRepository.GetPublicGlobalAsync(token.HighlightsOnly, null, null, cancellationToken);
        }

        return source;
    }

    private async Task ValidateCreatePermissionsAsync(Guid userId, CreateCalendarFeedTokenDto dto, CancellationToken cancellationToken)
    {
        switch (dto.Scope)
        {
            case CalendarFeedScope.Club:
                if (!dto.ClubId.HasValue)
                {
                    throw new InvalidOperationException("ClubId is required for club feed tokens.");
                }

                if (!await _authorizationService.CanEditClubAsync(userId, dto.ClubId.Value, cancellationToken))
                {
                    throw new UnauthorizedAccessException("No permission for this club.");
                }

                break;

            case CalendarFeedScope.Department:
                if (!dto.DepartmentId.HasValue)
                {
                    throw new InvalidOperationException("DepartmentId is required for department feed tokens.");
                }

                if (!await _authorizationService.CanEditDepartmentAsync(userId, dto.DepartmentId.Value, cancellationToken))
                {
                    throw new UnauthorizedAccessException("No permission for this department.");
                }

                break;

            case CalendarFeedScope.Global:
                await EnsureManagerOrAdminAsync(userId, cancellationToken);
                break;

            default:
                throw new InvalidOperationException("Unsupported feed scope.");
        }
    }

    private async Task EnsureTokenEditPermissionAsync(Guid userId, CalendarFeedToken token, CancellationToken cancellationToken)
    {
        switch (token.Scope)
        {
            case CalendarFeedScope.Club:
                if (!token.ClubId.HasValue || !await _authorizationService.CanEditClubAsync(userId, token.ClubId.Value, cancellationToken))
                {
                    throw new UnauthorizedAccessException("No permission for this club token.");
                }
                break;
            case CalendarFeedScope.Department:
                if (!token.DepartmentId.HasValue || !await _authorizationService.CanEditDepartmentAsync(userId, token.DepartmentId.Value, cancellationToken))
                {
                    throw new UnauthorizedAccessException("No permission for this department token.");
                }
                break;
            case CalendarFeedScope.Global:
                await EnsureManagerOrAdminAsync(userId, cancellationToken);
                break;
            default:
                throw new UnauthorizedAccessException("Unsupported feed scope.");
        }
    }

    private async Task<bool> IsManagerOrAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user != null && (user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager));
    }

    private async Task EnsureManagerOrAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsManagerOrAdminAsync(userId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Only admin or manager can manage global feed tokens.");
        }
    }

    private string BuildFeedUrl(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            return $"/api/calendar-feeds/{rawToken}.ics";
        }

        return $"{_baseUrl}/api/calendar-feeds/{rawToken}.ics";
    }

    private static string BuildFileName(CalendarFeedToken token)
    {
        return token.Scope switch
        {
            CalendarFeedScope.Club => token.IncludeDepartments ? "club-with-departments.ics" : "club.ics",
            CalendarFeedScope.Department => "department.ics",
            CalendarFeedScope.Global when token.HighlightsOnly => "vernetzt-highlights.ics",
            CalendarFeedScope.Global => "vernetzt-public.ics",
            _ => "calendar.ics"
        };
    }

    private string BuildIcs(IEnumerable<Event> events, CalendarFeedToken token)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//Igevo//Vernetzt//DE");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:PUBLISH");
        sb.AppendLine($"X-WR-CALNAME:{EscapeIcs(BuildCalendarName(token))}");

        var nowUtc = DateTime.UtcNow;
        foreach (var ev in events.OrderBy(e => e.StartUtc))
        {
            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:{ev.Id}@vernetzt.igevo");
            sb.AppendLine($"DTSTAMP:{FormatUtc(nowUtc)}");

            if (ev.IsAllDay)
            {
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ev.StartUtc, DateTimeKind.Utc), _berlinTimeZone).Date;
                var endLocalRaw = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ev.EndUtc, DateTimeKind.Utc), _berlinTimeZone);
                var endLocal = endLocalRaw.Date;
                if (endLocal <= startLocal)
                {
                    endLocal = startLocal.AddDays(1);
                }
                else if (endLocalRaw.TimeOfDay != TimeSpan.Zero)
                {
                    endLocal = endLocal.AddDays(1);
                }

                sb.AppendLine($"DTSTART;VALUE=DATE:{startLocal:yyyyMMdd}");
                sb.AppendLine($"DTEND;VALUE=DATE:{endLocal:yyyyMMdd}");
            }
            else
            {
                sb.AppendLine($"DTSTART:{FormatUtc(ev.StartUtc)}");
                sb.AppendLine($"DTEND:{FormatUtc(ev.EndUtc)}");
            }

            sb.AppendLine(FoldLine($"SUMMARY:{EscapeIcs(ev.Title)}"));

            if (!string.IsNullOrWhiteSpace(ev.Description))
            {
                sb.AppendLine(FoldLine($"DESCRIPTION:{EscapeIcs(ev.Description!)}"));
            }

            if (!string.IsNullOrWhiteSpace(ev.Location))
            {
                sb.AppendLine(FoldLine($"LOCATION:{EscapeIcs(ev.Location!)}"));
            }

            sb.AppendLine("END:VEVENT");
        }

        sb.AppendLine("END:VCALENDAR");
        return sb.ToString().Replace("\n", "\r\n");
    }

    private static string BuildCalendarName(CalendarFeedToken token)
    {
        return token.Scope switch
        {
            CalendarFeedScope.Club => "Vereinskalender",
            CalendarFeedScope.Department => "Abteilungskalender",
            CalendarFeedScope.Global when token.HighlightsOnly => "Vernetzt Highlights",
            CalendarFeedScope.Global => "Vernetzt Public",
            _ => "Vernetzt Kalender"
        };
    }

    private static string FormatUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'");
    }

    private static string EscapeIcs(string input)
    {
        return input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);
    }

    private static string FoldLine(string line)
    {
        const int max = 73;
        if (line.Length <= max)
        {
            return line;
        }

        var sb = new StringBuilder();
        var index = 0;
        while (index < line.Length)
        {
            var take = Math.Min(max, line.Length - index);
            if (index > 0)
            {
                sb.Append("\r\n ");
            }
            sb.Append(line, index, take);
            index += take;
        }

        return sb.ToString();
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return token;
    }

    private CalendarFeedTokenDto MapToken(CalendarFeedToken token)
    {
        var rawToken = TryUnprotectToken(token.ProtectedToken);

        return new CalendarFeedTokenDto
        {
            Id = token.Id,
            Scope = token.Scope,
            ClubId = token.ClubId,
            DepartmentId = token.DepartmentId,
            IncludeDepartments = token.IncludeDepartments,
            HighlightsOnly = token.HighlightsOnly,
            IsRevoked = token.IsRevoked,
            CreatedAt = token.CreatedAt,
            UpdatedAt = token.UpdatedAt,
            FeedUrl = !token.IsRevoked && !string.IsNullOrWhiteSpace(rawToken) ? BuildFeedUrl(rawToken) : null
        };
    }

    private string ProtectToken(string rawToken)
    {
        return _tokenProtector.Protect(rawToken);
    }

    private string? TryUnprotectToken(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken))
        {
            return null;
        }

        try
        {
            return _tokenProtector.Unprotect(protectedToken);
        }
        catch
        {
            return null;
        }
    }

    private static TimeZoneInfo ResolveBerlinTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
    }
}
