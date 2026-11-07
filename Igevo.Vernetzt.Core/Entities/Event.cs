using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Calendar event assigned to either a club or a department.
/// </summary>
public class Event : BaseEntity
{
    /// <summary>
    /// Event title.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional event description.
    /// </summary>
    [MaxLength(5000)]
    public string? Description { get; set; }

    /// <summary>
    /// Optional event location.
    /// </summary>
    [MaxLength(300)]
    public string? Location { get; set; }

    /// <summary>
    /// Event start date and time in UTC.
    /// </summary>
    public DateTime StartUtc { get; set; }

    /// <summary>
    /// Event end date and time in UTC.
    /// </summary>
    public DateTime EndUtc { get; set; }

    /// <summary>
    /// True when event spans whole local day(s).
    /// </summary>
    public bool IsAllDay { get; set; }

    /// <summary>
    /// Marks the event as highlighted for public/global calendars.
    /// </summary>
    public bool IsHighlight { get; set; }

    /// <summary>
    /// Optional club assignment. Mutually exclusive with DepartmentId.
    /// </summary>
    public Guid? ClubId { get; set; }

    /// <summary>
    /// Optional department assignment. Mutually exclusive with ClubId.
    /// </summary>
    public Guid? DepartmentId { get; set; }

    public virtual Club? Club { get; set; }
    public virtual Department? Department { get; set; }
    public virtual ICollection<EventMedia> Media { get; set; } = [];
}
