using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.DTOs.Users;

public class RequestAccountEmailChangeDto
{
    [Required(ErrorMessage = "E-Mail ist erforderlich")]
    [EmailAddress(ErrorMessage = "Ungültige E-Mail-Adresse")]
    [StringLength(255, ErrorMessage = "E-Mail darf maximal 255 Zeichen lang sein")]
    public string NewEmail { get; set; } = string.Empty;
}

public enum AccountEmailChangeStatus
{
    ConfirmationSent,
    Forbidden,
    NotFound,
    Invalid,
    Conflict,
    SendFailed
}

public record AccountEmailChangeRequestResult(AccountEmailChangeStatus Status, string Message);

public class AccountEmailChangeConfirmationResultDto
{
    public bool Success { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
