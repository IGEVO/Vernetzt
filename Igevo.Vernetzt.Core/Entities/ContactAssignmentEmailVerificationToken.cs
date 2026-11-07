namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// One-time verification token for a contact assignment context email.
/// </summary>
public class ContactAssignmentEmailVerificationToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the assignment this token belongs to.
    /// </summary>
    public Guid ContactAssignmentId { get; set; }

    public virtual ContactAssignment ContactAssignment { get; set; } = null!;

    /// <summary>
    /// SHA-256 hash of the raw token value.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Snapshot of normalized email when token was issued.
    /// Prevents verification if assignment email changed in between.
    /// </summary>
    public string EmailNormalizedSnapshot { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
