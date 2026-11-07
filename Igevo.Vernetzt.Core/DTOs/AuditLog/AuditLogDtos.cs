using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.AuditLog;

public class AuditLogDto
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? EntityDisplayName { get; set; }
    public AuditAction Action { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Notes { get; set; }
    public string? ResolvedOldValues { get; set; }
    public string? ResolvedNewValues { get; set; }
    public string? ResolvedNotes { get; set; }
}

public class AuditLogSummaryDto
{
    public int TotalEntries { get; set; }
    public Dictionary<string, int> ActionCounts { get; set; } = new();
    public Dictionary<string, int> EntityTypeCounts { get; set; } = new();
    public List<DailyActivityDto> DailyActivity { get; set; } = new();
}

public class DailyActivityDto
{
    public string Date { get; set; } = string.Empty;
    public int Count { get; set; }
}
