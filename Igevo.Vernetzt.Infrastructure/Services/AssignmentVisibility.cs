using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// The viewer of a contact assignment, reduced to what the visibility rules need.
/// </summary>
/// <param name="UserId">Signed-in user, or null for anonymous (public) access.</param>
/// <param name="IsAdminOrManager">SystemAdmin or Manager: sees everything, including pending assignments.</param>
/// <param name="IsDataReader">DataReader: sees every confirmed assignment regardless of its level.</param>
/// <param name="CanEditEntity">Has edit rights for the club/department the assignment belongs to.</param>
public readonly record struct AssignmentViewer(
    Guid? UserId,
    bool IsAdminOrManager = false,
    bool IsDataReader = false,
    bool CanEditEntity = false)
{
    public static AssignmentViewer Anonymous => new(null);

    public static AssignmentViewer For(User? user, bool canEditEntity = false) => user is null
        ? Anonymous
        : new AssignmentViewer(
            user.Id,
            user.Claims.Contains(UserClaim.SystemAdmin) || user.Claims.Contains(UserClaim.Manager),
            user.Claims.Contains(UserClaim.DataReader),
            canEditEntity);

    /// <summary>Same viewer, but with edit rights evaluated for another entity.</summary>
    public AssignmentViewer WithCanEditEntity(bool canEditEntity) => this with { CanEditEntity = canEditEntity };
}

/// <summary>
/// Visibility rules for contact assignments. The assigned user decides per assignment who may see it
/// (and its role/office address); a pending (not yet consented) assignment is only visible internally.
/// </summary>
public static class AssignmentVisibility
{
    /// <summary>
    /// Whether the viewer may see that the user is assigned to the entity.
    /// </summary>
    public static bool CanSeeAssignment(ContactAssignment assignment, AssignmentViewer viewer)
    {
        if (viewer.UserId.HasValue && viewer.UserId == assignment.UserId) return true;
        if (viewer.IsAdminOrManager || viewer.CanEditEntity) return true;
        if (!assignment.IsConfirmed) return false;
        if (viewer.IsDataReader) return true;
        return IsLevelVisible(assignment.Visibility, viewer);
    }

    /// <summary>
    /// Whether the viewer may see a personal field (primary email, phone) of an assigned user.
    /// The more restrictive of the assignment's and the field's visibility applies.
    /// </summary>
    public static bool CanSeeField(ContactAssignment assignment, VisibilityLevel fieldVisibility, AssignmentViewer viewer)
    {
        if (!CanSeeAssignment(assignment, viewer)) return false;
        if (viewer.UserId.HasValue && viewer.UserId == assignment.UserId) return true;
        if (viewer.IsAdminOrManager || viewer.IsDataReader) return true;
        var effective = assignment.IsConfirmed
            ? (VisibilityLevel)Math.Min((int)assignment.Visibility, (int)fieldVisibility)
            : VisibilityLevel.Internal;
        // Editors of the entity see the assignment, but personal fields still follow the user's own setting
        return IsLevelVisible(viewer.CanEditEntity ? fieldVisibility : effective, viewer);
    }

    /// <summary>
    /// Whether the viewer may see the (verified) role/office address. It follows the assignment's visibility.
    /// </summary>
    public static bool CanSeeContextEmail(ContactAssignment assignment, AssignmentViewer viewer) =>
        CanSeeAssignment(assignment, viewer);

    /// <summary>
    /// Whether the assignment and its role/office address appear on public pages.
    /// </summary>
    public static bool IsPublic(ContactAssignment assignment) =>
        assignment.IsConfirmed && assignment.Visibility == VisibilityLevel.Public;

    private static bool IsLevelVisible(VisibilityLevel level, AssignmentViewer viewer) => level switch
    {
        VisibilityLevel.Public => true,
        VisibilityLevel.AllUsers => viewer.UserId.HasValue,
        _ => false
    };
}

/// <summary>
/// Applies <see cref="AssignmentVisibility"/> to many assignments for one viewer. Edit rights for the
/// assignment's entity are only evaluated when they matter (pending or not visible otherwise) and cached.
/// </summary>
public sealed class AssignmentVisibilityCheck(
    AssignmentViewer viewer,
    IAuthorizationService authorizationService,
    Guid? viewerUserId)
{
    private readonly Dictionary<Guid, bool> _canEditEntity = new();

    public async Task<bool> AssignmentAsync(ContactAssignment assignment, CancellationToken cancellationToken) =>
        AssignmentVisibility.CanSeeAssignment(assignment, viewer)
        || (await CanEditEntityAsync(assignment, cancellationToken)
            && AssignmentVisibility.CanSeeAssignment(assignment, viewer.WithCanEditEntity(true)));

    public async Task<bool> ContextEmailAsync(ContactAssignment assignment, CancellationToken cancellationToken) =>
        await AssignmentAsync(assignment, cancellationToken);

    private async Task<bool> CanEditEntityAsync(ContactAssignment assignment, CancellationToken cancellationToken)
    {
        if (viewerUserId is not { } userId) return false;
        var entityId = assignment.ClubId ?? assignment.DepartmentId;
        if (entityId is not { } id) return false;
        if (_canEditEntity.TryGetValue(id, out var cached)) return cached;

        var canEdit = assignment.ClubId.HasValue
            ? await authorizationService.CanEditClubAsync(userId, id, cancellationToken)
            : await authorizationService.CanEditDepartmentAsync(userId, id, cancellationToken);
        _canEditEntity[id] = canEdit;
        return canEdit;
    }
}
