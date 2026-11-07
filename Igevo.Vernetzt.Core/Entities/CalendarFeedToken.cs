using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Token configuration for anonymous calendar feed access.
/// </summary>
public class CalendarFeedToken : BaseEntity
{
    /// <summary>
    /// SHA-256 hash of the raw token.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Protected (encrypted) raw token for authorized management views.
    /// </summary>
    [MaxLength(2000)]
    public string? ProtectedToken { get; set; }

    /// <summary>
    /// Feed scope.
    /// </summary>
    public CalendarFeedScope Scope { get; set; }

    /// <summary>
    /// Club for scope Club.
    /// </summary>
    public Guid? ClubId { get; set; }

    /// <summary>
    /// Department for scope Department.
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// For club scope: include department events of that club.
    /// </summary>
    public bool IncludeDepartments { get; set; } = true;

    /// <summary>
    /// For global scope: include only highlighted events.
    /// </summary>
    public bool HighlightsOnly { get; set; }

    /// <summary>
    /// Token revocation flag.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Timestamp when token was revoked.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    public virtual Club? Club { get; set; }
    public virtual Department? Department { get; set; }
}
