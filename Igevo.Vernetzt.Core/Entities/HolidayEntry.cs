using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// School holiday or public holiday shown as a background layer in the calendar views.
/// Deliberately separate from <see cref="Event"/>: holidays never appear in iCal exports.
/// </summary>
public class HolidayEntry : BaseEntity
{
    /// <summary>
    /// Name, e.g. "Herbstferien" or "Tag der Deutschen Einheit".
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// First day (inclusive).
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Last day (inclusive).
    /// </summary>
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Subscription the entry was imported from; null for manually maintained entries.
    /// Imported entries are read-only and replaced on every synchronization.
    /// </summary>
    public Guid? SourceId { get; set; }

    /// <summary>
    /// UID of the imported iCal event (plus occurrence date for recurring events).
    /// </summary>
    [MaxLength(500)]
    public string? ExternalUid { get; set; }

    public virtual HolidaySource? Source { get; set; }
}
