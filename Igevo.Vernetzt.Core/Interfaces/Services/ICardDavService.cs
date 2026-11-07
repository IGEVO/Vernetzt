using Igevo.Vernetzt.Core.DTOs.CardDav;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Read-only CardDAV address book per user: access management (app password) and vCard generation.
/// </summary>
public interface ICardDavService
{
    Task<CardDavStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new app password; an existing one is revoked.
    /// </summary>
    Task<CardDavAccessCreatedDto> CreateAccessAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the active app password. Returns false if there was none.
    /// </summary>
    /// <param name="reason">Optional reason for the audit log, e.g. "logout on all devices".</param>
    Task<bool> RevokeAccessAsync(Guid userId, string? reason = null, CancellationToken cancellationToken = default);

    Task<CardDavStatusDto> UpdateSettingsAsync(Guid userId, UpdateCardDavSettingsDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks username (email) and app password. Returns the user id or null.
    /// </summary>
    Task<Guid?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// The address book with all contacts visible to the user (with email or phone) and, if enabled,
    /// cards for clubs, departments and groups.
    /// </summary>
    Task<CardDavAddressBook> GetAddressBookAsync(Guid userId, CancellationToken cancellationToken = default);
}
