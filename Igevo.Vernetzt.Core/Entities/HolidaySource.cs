using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// External iCal subscription (e.g. school holidays or public holidays of a federal state) whose
/// entries are imported into the central holiday calendar.
/// </summary>
public class HolidaySource : BaseEntity
{
    /// <summary>
    /// Display name, e.g. "Schulferien NRW".
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// HTTPS URL of the iCal feed.
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Disabled sources are neither synchronized nor shown in calendars.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Time of the last successful synchronization (UTC).
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Error of the last synchronization attempt; null when it succeeded.
    /// </summary>
    [MaxLength(1000)]
    public string? LastSyncError { get; set; }

    public virtual ICollection<HolidayEntry> Entries { get; set; } = [];
}
