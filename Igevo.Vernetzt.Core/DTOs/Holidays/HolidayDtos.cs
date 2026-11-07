using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.DTOs.Holidays;

/// <summary>
/// School holiday or public holiday for the calendar views.
/// </summary>
public class HolidayEntryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>First day (inclusive).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Last day (inclusive).</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Subscription the entry was imported from; null for manual entries.</summary>
    public Guid? SourceId { get; set; }

    public string? SourceName { get; set; }
}

/// <summary>
/// Create or update a manually maintained holiday entry.
/// </summary>
public class SaveHolidayEntryDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    /// <summary>Last day (inclusive); equal to <see cref="StartDate"/> for a single day.</summary>
    public DateOnly EndDate { get; set; }
}

/// <summary>
/// External iCal subscription with its synchronization status.
/// </summary>
public class HolidaySourceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }
    public int EntryCount { get; set; }
}

/// <summary>
/// Create or update an external iCal subscription.
/// </summary>
public class SaveHolidaySourceDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>HTTPS URL of the iCal feed (webcal:// is accepted and converted).</summary>
    [Required]
    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
}
