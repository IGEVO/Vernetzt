using System.ComponentModel.DataAnnotations;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.DTOs.Users;

public class UserDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "E-Mail ist erforderlich")]
    [EmailAddress(ErrorMessage = "Ungültige E-Mail-Adresse")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vorname ist erforderlich")]
    [StringLength(100, ErrorMessage = "Vorname darf maximal 100 Zeichen lang sein")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nachname ist erforderlich")]
    [StringLength(100, ErrorMessage = "Nachname darf maximal 100 Zeichen lang sein")]
    public string LastName { get; set; } = string.Empty;

    public string FullName => $"{FirstName} {LastName}";

    [Phone(ErrorMessage = "Ungültige Telefonnummer")]
    [StringLength(50, ErrorMessage = "Telefonnummer darf maximal 50 Zeichen lang sein")]
    public string? PhoneNumber { get; set; }

    public VisibilityLevel EmailVisibility { get; set; }
    public VisibilityLevel PhoneVisibility { get; set; }

    public bool EmailVerified { get; set; }

    /// <summary>
    /// Indicates whether the user has an avatar image.
    /// </summary>
    public bool HasAvatar { get; set; }

    /// <summary>
    /// Number of registered passkeys. Only filled in the user administration list, otherwise null.
    /// </summary>
    public int? PasskeyCount { get; set; }

    public List<UserClaim> Claims { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class CreateUserDto
{
    [Required(ErrorMessage = "E-Mail ist erforderlich")]
    [EmailAddress(ErrorMessage = "Ungültige E-Mail-Adresse")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vorname ist erforderlich")]
    [StringLength(100, ErrorMessage = "Vorname darf maximal 100 Zeichen lang sein")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nachname ist erforderlich")]
    [StringLength(100, ErrorMessage = "Nachname darf maximal 100 Zeichen lang sein")]
    public string LastName { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Ungültige Telefonnummer")]
    [StringLength(50, ErrorMessage = "Telefonnummer darf maximal 50 Zeichen lang sein")]
    public string? PhoneNumber { get; set; }

    public VisibilityLevel EmailVisibility { get; set; } = VisibilityLevel.AllUsers;
    public VisibilityLevel PhoneVisibility { get; set; } = VisibilityLevel.AllUsers;
}

public class UpdateUserDto
{
    [Required(ErrorMessage = "Vorname ist erforderlich")]
    [StringLength(100, ErrorMessage = "Vorname darf maximal 100 Zeichen lang sein")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nachname ist erforderlich")]
    [StringLength(100, ErrorMessage = "Nachname darf maximal 100 Zeichen lang sein")]
    public string LastName { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Ungültige Telefonnummer")]
    [StringLength(50, ErrorMessage = "Telefonnummer darf maximal 50 Zeichen lang sein")]
    public string? PhoneNumber { get; set; }

    public VisibilityLevel EmailVisibility { get; set; }
    public VisibilityLevel PhoneVisibility { get; set; }
}

public class AssignClaimDto
{
    [Required(ErrorMessage = "Rolle ist erforderlich")]
    public UserClaim Claim { get; set; }
}

public class AssignResourceDto
{
    [Required(ErrorMessage = "Ressourcen-ID ist erforderlich")]
    public Guid ResourceId { get; set; }
}
