using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Entities;

/// <summary>
/// Represents a user/contact person in the system.
/// Every contact person is also a user, but not every user is necessarily a contact person for a club/department.
/// </summary>
public class User : BaseEntity
{
    /// <summary>
    /// First name of the user.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Last name of the user.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Full name (computed property, not stored in DB).
    /// </summary>
    public string FullName => $"{FirstName} {LastName}";

    /// <summary>
    /// User's email address (used for authentication).
    /// Must be unique (case-insensitive via database collation).
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Visibility level for the user's email address.
    /// </summary>
    public VisibilityLevel EmailVisibility { get; set; } = VisibilityLevel.AllUsers;

    /// <summary>
    /// Phone number.
    /// </summary>
    [MaxLength(50)]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Visibility level for the user's phone number.
    /// </summary>
    public VisibilityLevel PhoneVisibility { get; set; } = VisibilityLevel.AllUsers;

    /// <summary>
    /// Whether the user's email has been verified.
    /// </summary>
    public bool EmailVerified { get; set; } = false;

    /// <summary>
    /// Last login timestamp (UTC).
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Claims/roles assigned to this user for authorization (stored as JSON).
    /// </summary>
    public List<UserClaim> Claims { get; set; } = [];

    /// <summary>
    /// Whether the user has uploaded an avatar image.
    /// </summary>
    public bool HasAvatar { get; set; } = false;

    /// <summary>
    /// Preferred way to sign in, chosen during onboarding (used to tailor the welcome flow).
    /// </summary>
    public LoginMethod PreferredLoginMethod { get; set; } = LoginMethod.EmailLink;

    /// <summary>
    /// CardDAV address book: also provide cards for clubs, departments and groups.
    /// </summary>
    public bool CardDavIncludeOrganizations { get; set; } = false;

    // Navigation properties

    /// <summary>
    /// Contact assignments where this user is the contact person.
    /// </summary>
    public virtual ICollection<ContactAssignment> ContactAssignments { get; set; } = [];
}
