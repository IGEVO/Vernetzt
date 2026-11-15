namespace Igevo.Vernetzt.Core.Exceptions;

/// <summary>
/// The user is already assigned to the club/department (confirmed or still waiting for consent).
/// The message is user-facing (German).
/// </summary>
public class ContactAssignmentConflictException(string message) : InvalidOperationException(message)
{
    public static ContactAssignmentConflictException For(bool pending) => new(pending
        ? "Diese Person ist hier bereits zugeordnet, die Zuordnung wartet noch auf ihre Zustimmung. Du kannst sie erneut daran erinnern."
        : "Diese Person ist hier bereits als Kontaktperson zugeordnet.");
}
