using Igevo.Vernetzt.Core.DTOs.Users;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Changes the personal (login) email address of an account via a confirmation link sent to the new address.
/// </summary>
public interface IAccountEmailChangeService
{
    /// <summary>
    /// Requests a change of the login address of <paramref name="userId"/> and sends a confirmation link to the new address.
    /// Users may change their own address; SystemAdmins may change any address, Managers any address except a SystemAdmin's.
    /// </summary>
    Task<AccountEmailChangeRequestResult> RequestChangeAsync(
        Guid requestingUserId,
        Guid userId,
        string newEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the change behind a confirmation token and notifies the previous address.
    /// </summary>
    Task<AccountEmailChangeConfirmationResultDto> ConfirmChangeAsync(
        string token,
        CancellationToken cancellationToken = default);
}
