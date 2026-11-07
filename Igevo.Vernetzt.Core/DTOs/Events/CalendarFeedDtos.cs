using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Events;

public class CreateCalendarFeedTokenDto
{
    [Required(ErrorMessage = "Scope ist erforderlich")]
    public CalendarFeedScope Scope { get; set; }

    public Guid? ClubId { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool IncludeDepartments { get; set; } = true;
    public bool HighlightsOnly { get; set; }
}

public class CalendarFeedTokenDto
{
    public Guid Id { get; set; }
    public CalendarFeedScope Scope { get; set; }
    public Guid? ClubId { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool IncludeDepartments { get; set; }
    public bool HighlightsOnly { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? FeedUrl { get; set; }
}

public class CalendarFeedTokenCreatedDto
{
    public CalendarFeedTokenDto Token { get; set; } = new();
    public string FeedUrl { get; set; } = string.Empty;
}

public class CalendarFeedContentDto
{
    public string FileName { get; set; } = "calendar.ics";
    public string Content { get; set; } = string.Empty;
}
