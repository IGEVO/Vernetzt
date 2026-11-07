using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.Gdpr;
using Igevo.Vernetzt.Core.DTOs.Invitations;
using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for managing users.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Gets all users (SystemAdmin only).
    /// </summary>
    Task<IEnumerable<User>> GetAllAsync(Guid requestingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by ID if requesting user has permission.
    /// </summary>
    Task<User?> GetByIdAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current user profile.
    /// </summary>
    Task<User?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user (SystemAdmin only).
    /// </summary>
    Task<User> CreateAsync(Guid requestingUserId, User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a user profile.
    /// </summary>
    Task<User?> UpdateAsync(Guid requestingUserId, Guid userId, User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user (SystemAdmin only).
    /// </summary>
    Task<bool> DeleteAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a claim/role to a user (SystemAdmin only).
    /// </summary>
    Task<bool> AssignClaimAsync(Guid requestingUserId, Guid userId, UserClaim claim, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a claim/role from a user (SystemAdmin only).
    /// </summary>
    Task<bool> RemoveClaimAsync(Guid requestingUserId, Guid userId, UserClaim claim, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all contacts with their organization assignments.
    /// </summary>
    Task<IEnumerable<ContactDto>> GetAllContactsAsync(Guid requestingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets assignment-based recipient candidates for email flows.
    /// A single user can appear multiple times (once per contact assignment).
    /// </summary>
    Task<IEnumerable<RecipientCandidateDto>> GetRecipientCandidatesAsync(Guid requestingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches contacts with server-side filtering and pagination.
    /// Searches by name, club, department, and position.
    /// </summary>
    Task<PagedResult<ContactDto>> SearchContactsAsync(Guid requestingUserId, ContactSearchParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all contact assignments where the requesting user is assigned.
    /// Includes context-email and verification metadata for self-service management.
    /// </summary>
    Task<IEnumerable<MyContactAssignmentDto>> GetMyContactAssignmentsAsync(Guid requestingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets contact assignments for a specific user.
    /// Only SystemAdmin/Manager may access this in user administration.
    /// </summary>
    Task<IEnumerable<MyContactAssignmentDto>> GetUserContactAssignmentsAsync(
        Guid requestingUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates context email and visibility for one own assignment.
    /// Sends verification when email is newly set/changed.
    /// </summary>
    Task<UpdateMyContactAssignmentEmailResponse> UpdateMyContactAssignmentEmailAsync(
        Guid requestingUserId,
        Guid assignmentId,
        UpdateMyContactAssignmentEmailDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates context email and visibility for one assignment of a target user.
    /// Only SystemAdmin/Manager may use this in user administration.
    /// </summary>
    Task<UpdateMyContactAssignmentEmailResponse> UpdateUserContactAssignmentEmailAsync(
        Guid requestingUserId,
        Guid targetUserId,
        Guid assignmentId,
        UpdateMyContactAssignmentEmailDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resends context-email verification for one own assignment.
    /// </summary>
    Task<UpdateMyContactAssignmentEmailResponse> ResendMyContactAssignmentEmailVerificationAsync(
        Guid requestingUserId,
        Guid assignmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resends context-email verification for one assignment of a target user.
    /// Only SystemAdmin/Manager may use this in user administration.
    /// </summary>
    Task<UpdateMyContactAssignmentEmailResponse> ResendUserContactAssignmentEmailVerificationAsync(
        Guid requestingUserId,
        Guid targetUserId,
        Guid assignmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a context-email token and activates the assignment context email.
    /// </summary>
    Task<ContactAssignmentEmailVerificationResultDto> VerifyContactAssignmentEmailAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Export all user data for GDPR compliance (Right to Data Portability).
    /// </summary>
    Task<UserDataExport> ExportUserDataAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Export all user data as ZIP archive: the export JSON plus the user's media files referenced from it.
    /// </summary>
    Task<byte[]> ExportUserDataArchiveAsync(Guid requestingUserId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete user account and all associated data (Right to be Forgotten).
    /// Returns false if user cannot be deleted (e.g., last admin).
    /// </summary>
    Task<DeleteAccountResponse> DeleteAccountAsync(Guid requestingUserId, Guid userId, string confirmEmail, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invite a user by email, optionally pre-linking a contact assignment.
    /// Any authenticated user can invite others.
    /// </summary>
    Task<InvitationResponse> InviteUserAsync(Guid requestingUserId, InviteUserDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accept an invitation and complete onboarding.
    /// Creates user account and all pre-linked contact assignments from ALL valid invitations for the email.
    /// </summary>
    Task<AcceptInvitationResponse> AcceptInvitationAsync(AcceptInvitationDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke (delete) an invitation token. Callable by the invited user via a unique link.
    /// </summary>
    Task<bool> RevokeInvitationAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get invitation details by token for the onboarding page.
    /// Returns aggregated pending assignments from all valid invitations for the email.
    /// </summary>
    Task<InvitationDetailsResponse?> GetInvitationDetailsAsync(string token, CancellationToken cancellationToken = default);
}
