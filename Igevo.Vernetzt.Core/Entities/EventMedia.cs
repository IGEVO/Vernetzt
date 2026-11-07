using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

public class EventMedia : BaseEntity
{
    public Guid EventId { get; set; }

    public EventMediaType Type { get; set; } = EventMediaType.Attachment;

    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Horizontal focal point (0 = left, 1 = right). Used for object-position when the
    /// image is shown in a different aspect ratio than its original.
    /// </summary>
    public double FocusX { get; set; } = 0.5;

    /// <summary>
    /// Views this media item is explicitly used for (overrides the title image for these views).
    /// </summary>
    public EventMediaUsage Usages { get; set; } = EventMediaUsage.None;

    /// <summary>
    /// Vertical focal point (0 = top, 1 = bottom).
    /// </summary>
    public double FocusY { get; set; } = 0.5;

    /// <summary>
    /// Pixel width of the preview image (image itself or rendered first PDF page). Null if no preview exists.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Pixel height of the preview image. Null if no preview exists.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Comma separated list of generated WebP preview widths (e.g. "160,480,960,1600").
    /// Null or empty if no preview could be generated.
    /// </summary>
    [MaxLength(100)]
    public string? VariantWidths { get; set; }

    public virtual Event Event { get; set; } = null!;

    public IReadOnlyList<int> GetVariantWidths() =>
        string.IsNullOrWhiteSpace(VariantWidths)
            ? []
            : VariantWidths.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => int.TryParse(v, out var w) ? w : 0)
                .Where(w => w > 0)
                .OrderBy(w => w)
                .ToList();

    public bool HasPreview => !string.IsNullOrWhiteSpace(VariantWidths);

    public static string GetVariantPath(Guid eventId, Guid mediaId, int width) =>
        $"event-media/{eventId}/variants/{mediaId}/w{width}.webp";

    public static string GetVariantDirectory(Guid eventId, Guid mediaId) =>
        $"event-media/{eventId}/variants/{mediaId}";
}
