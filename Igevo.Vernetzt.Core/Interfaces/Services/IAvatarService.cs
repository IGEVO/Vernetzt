namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for managing avatars for users, clubs, and departments.
/// </summary>
public interface IAvatarService
{
    /// <summary>
    /// Uploads and processes an avatar for a user.
    /// </summary>
    Task<AvatarUploadResult> UploadUserAvatarAsync(Guid userId, Stream imageStream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads and processes an avatar for a club.
    /// </summary>
    Task<AvatarUploadResult> UploadClubAvatarAsync(Guid clubId, Stream imageStream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads and processes an avatar for a department.
    /// </summary>
    Task<AvatarUploadResult> UploadDepartmentAvatarAsync(Guid departmentId, Stream imageStream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the full-size avatar of a user, e.g. for the GDPR data export.
    /// </summary>
    /// <returns>The image bytes, or null if the user has no avatar.</returns>
    Task<byte[]?> GetUserAvatarAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the original club avatar (logo) as WebP bytes, or null if none exists.
    /// </summary>
    Task<byte[]?> GetClubAvatarAsync(Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the original department/group avatar (logo) as WebP bytes, or null if none exists.
    /// Does not fall back to the club.
    /// </summary>
    Task<byte[]?> GetDepartmentAvatarAsync(Guid departmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the avatar for a user.
    /// </summary>
    Task<bool> DeleteUserAvatarAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the avatar for a club.
    /// </summary>
    Task<bool> DeleteClubAvatarAsync(Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the avatar for a department.
    /// </summary>
    Task<bool> DeleteDepartmentAvatarAsync(Guid departmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the avatar directory path for a department, with fallback to the parent club's avatar.
    /// </summary>
    /// <returns>The directory path (e.g. "avatars/departments/{id}" or "avatars/clubs/{clubId}"), or null if no avatar exists.</returns>
    Task<string?> GetDepartmentAvatarDirectoryAsync(Guid departmentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an avatar upload operation.
/// </summary>
public class AvatarUploadResult
{
    /// <summary>
    /// Whether the upload was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if upload failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
