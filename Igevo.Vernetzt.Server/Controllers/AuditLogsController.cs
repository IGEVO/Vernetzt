using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.AuditLog;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AuthService = Igevo.Vernetzt.Core.Interfaces.Services.IAuthorizationService;

namespace Igevo.Vernetzt.Server.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUserRepository _userRepository;
    private readonly VernetztDbContext _dbContext;
    private readonly AuthService _authorizationService;
    private readonly ILogger<AuditLogsController> _logger;
    private static readonly Regex GuidRegex = new(
        @"(?<![A-Fa-f0-9])[A-Fa-f0-9]{8}\-[A-Fa-f0-9]{4}\-[A-Fa-f0-9]{4}\-[A-Fa-f0-9]{4}\-[A-Fa-f0-9]{12}(?![A-Fa-f0-9])",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public AuditLogsController(
        IAuditLogRepository auditLogRepository,
        IUserRepository userRepository,
        VernetztDbContext dbContext,
        AuthService authorizationService,
        ILogger<AuditLogsController> logger)
    {
        _auditLogRepository = auditLogRepository;
        _userRepository = userRepository;
        _dbContext = dbContext;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    /// <summary>
    /// Gets paginated audit logs with optional filtering. SystemAdmin only.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? entityType = null,
        [FromQuery] AuditAction? action = null,
        [FromQuery] bool includeReadAccess = false,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.IsSystemAdminAsync(cancellationToken))
            return Forbid();

        try
        {
            var all = (await _auditLogRepository.GetAllAsync(cancellationToken)).AsQueryable();
            var effectiveIncludeReadAccess = includeReadAccess || action == AuditAction.View;

            if (!effectiveIncludeReadAccess)
                all = all.Where(l => l.Action != AuditAction.View);

            if (!string.IsNullOrEmpty(entityType))
                all = all.Where(l => l.EntityType == entityType);

            if (action.HasValue)
                all = all.Where(l => l.Action == action.Value);

            var ordered = all.OrderByDescending(l => l.Timestamp).ToList();
            var totalCount = ordered.Count;

            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 50;

            var paginated = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Enrich with user names
            var userIds = paginated
                .Select(l => l.UserId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            var userNames = new Dictionary<Guid, string>();
            foreach (var uid in userIds)
            {
                var user = await _userRepository.GetByIdAsync(uid, cancellationToken);
                if (user != null)
                    userNames[uid] = BuildDisplayName(user.FirstName, user.LastName, user.Email);
            }

            var guidCandidates = new HashSet<Guid>();
            foreach (var log in paginated)
            {
                if (log.UserId != Guid.Empty) guidCandidates.Add(log.UserId);
                if (log.EntityId != Guid.Empty) guidCandidates.Add(log.EntityId);
                AddGuidsFromText(log.OldValues, guidCandidates);
                AddGuidsFromText(log.NewValues, guidCandidates);
                AddGuidsFromText(log.Notes, guidCandidates);
            }

            var guidNameMap = await BuildGuidNameMapAsync(guidCandidates, cancellationToken);
            foreach (var kvp in userNames)
            {
                guidNameMap[kvp.Key] = kvp.Value;
            }

            var dtos = paginated.Select(l => new AuditLogDto
            {
                Id = l.Id,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                EntityDisplayName = ResolveEntityDisplayName(l.EntityType, l.EntityId, guidNameMap),
                Action = l.Action,
                UserId = l.UserId,
                UserName = l.UserId == Guid.Empty
                    ? "System"
                    : userNames.TryGetValue(l.UserId, out var name) ? name : null,
                Timestamp = l.Timestamp,
                IpAddress = l.IpAddress,
                OldValues = l.OldValues,
                NewValues = l.NewValues,
                Notes = l.Notes,
                ResolvedOldValues = ResolveText(l.OldValues, guidNameMap),
                ResolvedNewValues = ResolveText(l.NewValues, guidNameMap),
                ResolvedNotes = ResolveText(l.Notes, guidNameMap)
            });

            var result = PagedResult<AuditLogDto>.Create(dtos, totalCount, page, pageSize);
            return Ok(ApiResponse<PagedResult<AuditLogDto>>.CreateSuccess(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit logs");
            return StatusCode(500, ApiResponse<object>.CreateError("Fehler beim Abrufen der Audit-Logs."));
        }
    }

    /// <summary>
    /// Gets audit log summary statistics for the last N days. SystemAdmin only.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<AuditLogSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<AuditLogSummaryDto>>> GetSummary(
        [FromQuery] int days = 30,
        [FromQuery] bool includeReadAccess = false,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.IsSystemAdminAsync(cancellationToken))
            return Forbid();

        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-days);
            var all = (await _auditLogRepository.GetAllAsync(cancellationToken))
                .Where(l => l.Timestamp >= cutoff)
                .ToList();

            if (!includeReadAccess)
            {
                all = all.Where(l => l.Action != AuditAction.View).ToList();
            }

            var actionCounts = all
                .GroupBy(l => l.Action.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            var entityTypeCounts = all
                .GroupBy(l => l.EntityType)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());

            var dailyActivity = all
                .GroupBy(l => l.Timestamp.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DailyActivityDto
                {
                    Date = g.Key.ToString("dd.MM."),
                    Count = g.Count()
                })
                .ToList();

            var summary = new AuditLogSummaryDto
            {
                TotalEntries = all.Count,
                ActionCounts = actionCounts,
                EntityTypeCounts = entityTypeCounts,
                DailyActivity = dailyActivity
            };

            return Ok(ApiResponse<AuditLogSummaryDto>.CreateSuccess(summary));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit log summary");
            return StatusCode(500, ApiResponse<object>.CreateError("Fehler beim Abrufen der Zusammenfassung."));
        }
    }

    /// <summary>
    /// Gets distinct entity types present in the audit log (for filter dropdown). SystemAdmin only.
    /// </summary>
    [HttpGet("entity-types")]
    [ProducesResponseType(typeof(ApiResponse<List<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<List<string>>>> GetEntityTypes(
        [FromQuery] bool includeReadAccess = false,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.IsSystemAdminAsync(cancellationToken))
            return Forbid();

        try
        {
            var all = await _auditLogRepository.GetAllAsync(cancellationToken);
            if (!includeReadAccess)
            {
                all = all.Where(l => l.Action != AuditAction.View).ToList();
            }
            var entityTypes = all.Select(l => l.EntityType).Distinct().OrderBy(t => t).ToList();
            return Ok(ApiResponse<List<string>>.CreateSuccess(entityTypes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit log entity types");
            return StatusCode(500, ApiResponse<object>.CreateError("Fehler beim Abrufen der Entitätstypen."));
        }
    }

    private async Task<Dictionary<Guid, string>> BuildGuidNameMapAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, string>();
        var validIds = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (validIds.Count == 0)
        {
            return map;
        }

        var users = await _dbContext.Users.AsNoTracking()
            .Where(x => validIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FirstName, x.LastName, x.Email })
            .ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            map[user.Id] = BuildDisplayName(user.FirstName, user.LastName, user.Email);
        }

        var clubs = await _dbContext.Clubs.AsNoTracking()
            .Where(x => validIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);
        foreach (var club in clubs)
        {
            if (!string.IsNullOrWhiteSpace(club.Name))
                map[club.Id] = club.Name;
        }

        var departments = await _dbContext.Departments.AsNoTracking()
            .Where(x => validIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);
        foreach (var dept in departments)
        {
            if (!string.IsNullOrWhiteSpace(dept.Name))
                map[dept.Id] = dept.Name;
        }

        var clubTypes = await _dbContext.ClubTypes.AsNoTracking()
            .Where(x => validIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);
        foreach (var clubType in clubTypes)
        {
            if (!string.IsNullOrWhiteSpace(clubType.Name))
                map[clubType.Id] = clubType.Name;
        }

        var positions = await _dbContext.ContactPositions.AsNoTracking()
            .Where(x => validIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);
        foreach (var position in positions)
        {
            if (!string.IsNullOrWhiteSpace(position.Name))
                map[position.Id] = position.Name;
        }

        var invitations = await _dbContext.InvitationTokens.AsNoTracking()
            .Where(x => validIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Email })
            .ToListAsync(cancellationToken);
        foreach (var invitation in invitations)
        {
            if (!string.IsNullOrWhiteSpace(invitation.Email))
                map[invitation.Id] = $"Einladung ({invitation.Email})";
        }

        var relayTokens = await _dbContext.EmailRelayTokens.AsNoTracking()
            .Where(x => validIds.Contains(x.Id))
            .Select(x => new { x.Id, x.TokenAddress })
            .ToListAsync(cancellationToken);
        foreach (var relayToken in relayTokens)
        {
            if (!string.IsNullOrWhiteSpace(relayToken.TokenAddress))
                map[relayToken.Id] = $"Relay-Token ({relayToken.TokenAddress})";
        }

        var assignments = await _dbContext.ContactAssignments.AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Club)
            .Include(x => x.Department)
            .Where(x => validIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        foreach (var assignment in assignments)
        {
            var person = BuildDisplayName(
                assignment.User?.FirstName,
                assignment.User?.LastName,
                assignment.User?.Email);
            var org = assignment.Club?.Name ?? assignment.Department?.Name ?? "Unbekannte Zuordnung";
            map[assignment.Id] = $"Zuordnung ({person} / {org})";
        }

        return map;
    }

    private static string BuildDisplayName(string? firstName, string? lastName, string? fallbackEmail)
    {
        var fullName = $"{firstName} {lastName}".Trim();
        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;
        return string.IsNullOrWhiteSpace(fallbackEmail) ? "Unbekannt" : fallbackEmail;
    }

    private static void AddGuidsFromText(string? text, HashSet<Guid> target)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (Match match in GuidRegex.Matches(text))
        {
            if (Guid.TryParse(match.Value, out var id) && id != Guid.Empty)
            {
                target.Add(id);
            }
        }
    }

    private static string? ResolveEntityDisplayName(
        string entityType,
        Guid entityId,
        IReadOnlyDictionary<Guid, string> guidNameMap)
    {
        if (entityId == Guid.Empty)
            return null;
        if (guidNameMap.TryGetValue(entityId, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;
        return entityType switch
        {
            "BulkEmail" => "Bulk E-Mail",
            _ => null
        };
    }

    private static string? ResolveText(string? raw, IReadOnlyDictionary<Guid, string> guidNameMap)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        try
        {
            var node = JsonNode.Parse(raw);
            if (node == null)
                return ReplaceGuidsInPlainText(raw, guidNameMap);

            var replacedNode = ReplaceGuidsInJsonNode(node, guidNameMap);
            return replacedNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            return ReplaceGuidsInPlainText(raw, guidNameMap);
        }
    }

    private static JsonNode ReplaceGuidsInJsonNode(JsonNode node, IReadOnlyDictionary<Guid, string> guidNameMap)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (property.Value != null)
                {
                    obj[property.Key] = ReplaceGuidsInJsonNode(property.Value, guidNameMap);
                }
            }
            return obj;
        }

        if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                if (arr[i] != null)
                {
                    arr[i] = ReplaceGuidsInJsonNode(arr[i]!, guidNameMap);
                }
            }
            return arr;
        }

        if (node is JsonValue valueNode && valueNode.TryGetValue<string>(out var stringValue))
        {
            if (Guid.TryParse(stringValue, out var id) &&
                id != Guid.Empty &&
                guidNameMap.TryGetValue(id, out var name))
            {
                return JsonValue.Create($"{name} ({id})")!;
            }
        }

        return node;
    }

    private static string ReplaceGuidsInPlainText(string raw, IReadOnlyDictionary<Guid, string> guidNameMap)
    {
        return GuidRegex.Replace(raw, match =>
        {
            if (Guid.TryParse(match.Value, out var id) &&
                id != Guid.Empty &&
                guidNameMap.TryGetValue(id, out var name))
            {
                return $"{name} ({id})";
            }
            return match.Value;
        });
    }
}
