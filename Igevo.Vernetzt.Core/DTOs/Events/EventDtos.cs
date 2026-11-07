using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Events;

public class EventMediaDto
{
    public Guid Id { get; set; }
    public EventMediaType Type { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int SortOrder { get; set; }
    public string FileUrl { get; set; } = string.Empty;

    /// <summary>Focal point (0..1) for object-position when cropping into other aspect ratios.</summary>
    public double FocusX { get; set; } = 0.5;
    public double FocusY { get; set; } = 0.5;

    /// <summary>Views this media is explicitly assigned to (flags: 1 = list, 2 = header, 4 = card).</summary>
    public EventMediaUsage Usages { get; set; }

    /// <summary>Dimensions of the preview image (null if no preview exists, e.g. PDF without rendering).</summary>
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>Responsive WebP previews, ordered by width ascending. Empty for non-image media.</summary>
    public IReadOnlyList<EventMediaVariantDto> Variants { get; set; } = [];
}

public class EventMediaVariantDto
{
    public int Width { get; set; }
    public string Url { get; set; } = string.Empty;
}

public class UpdateEventMediaDto
{
    public EventMediaType? Type { get; set; }
    public double? FocusX { get; set; }
    public double? FocusY { get; set; }

    /// <summary>Replaces the explicit view assignments of this media; the given views are removed from other media.</summary>
    public EventMediaUsage? Usages { get; set; }
}

public class EventDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsHighlight { get; set; }
    public Guid? ClubId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? ClubName { get; set; }
    public string? DepartmentName { get; set; }

    /// <summary>Name of the parent department if the event belongs to a group.</summary>
    public string? ParentDepartmentName { get; set; }
    public bool CanEdit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public IReadOnlyList<EventMediaDto> Media { get; set; } = [];

    /// <summary>
    /// Resolved display image (Cover → Image → any media with preview). Null if the event has no usable image.
    /// </summary>
    public EventMediaDto? Image { get; set; }

    /// <summary>Image for list icons (explicit assignment, otherwise <see cref="Image"/>).</summary>
    public EventMediaDto? ListImage { get; set; }

    /// <summary>Image for the detail header (explicit assignment, otherwise <see cref="Image"/>).</summary>
    public EventMediaDto? HeaderImage { get; set; }

    /// <summary>Image for public overview cards (explicit assignment, otherwise <see cref="Image"/>).</summary>
    public EventMediaDto? CardImage { get; set; }

    /// <summary>The event's poster (usually A4 portrait), shown uncropped. Null if there is none or it has no preview.</summary>
    public EventMediaDto? PosterImage { get; set; }
}

public class CreateEventDto
{
    [Required(ErrorMessage = "Titel ist erforderlich")]
    [StringLength(200, ErrorMessage = "Titel darf maximal 200 Zeichen lang sein")]
    public string Title { get; set; } = string.Empty;

    [StringLength(5000, ErrorMessage = "Beschreibung darf maximal 5000 Zeichen lang sein")]
    public string? Description { get; set; }

    [StringLength(300, ErrorMessage = "Ort darf maximal 300 Zeichen lang sein")]
    public string? Location { get; set; }

    [Required(ErrorMessage = "Startdatum ist erforderlich")]
    public DateTime StartUtc { get; set; }

    [Required(ErrorMessage = "Enddatum ist erforderlich")]
    public DateTime EndUtc { get; set; }

    public bool IsAllDay { get; set; }
    public bool IsHighlight { get; set; }
    public Guid? ClubId { get; set; }
    public Guid? DepartmentId { get; set; }
}

public class UpdateEventDto
{
    [Required(ErrorMessage = "Titel ist erforderlich")]
    [StringLength(200, ErrorMessage = "Titel darf maximal 200 Zeichen lang sein")]
    public string Title { get; set; } = string.Empty;

    [StringLength(5000, ErrorMessage = "Beschreibung darf maximal 5000 Zeichen lang sein")]
    public string? Description { get; set; }

    [StringLength(300, ErrorMessage = "Ort darf maximal 300 Zeichen lang sein")]
    public string? Location { get; set; }

    [Required(ErrorMessage = "Startdatum ist erforderlich")]
    public DateTime StartUtc { get; set; }

    [Required(ErrorMessage = "Enddatum ist erforderlich")]
    public DateTime EndUtc { get; set; }

    public bool IsAllDay { get; set; }
    public bool IsHighlight { get; set; }
}
