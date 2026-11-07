using Igevo.Vernetzt.Core.DTOs.Users;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

public interface IRecipientResolutionService
{
    Task<RecipientResolutionResult> ResolveForSendAsync(
        Guid requestingUserId,
        IEnumerable<Guid> recipientAssignmentIds,
        IEnumerable<Guid> recipientUserIds,
        CancellationToken cancellationToken = default);

    Task<List<string>> ResolveDisplayItemsByEmailAsync(
        IEnumerable<string> recipientEmails,
        CancellationToken cancellationToken = default);
}

public class RecipientResolutionResult
{
    public bool Success { get; set; }
    public bool Forbidden { get; set; }
    public List<Guid> ForbiddenAssignmentIds { get; set; } = new();
    public List<string> RecipientEmails { get; set; } = new();
    public List<string> RecipientDisplayItems { get; set; } = new();
    public int RequestedRecipientCount { get; set; }
}
