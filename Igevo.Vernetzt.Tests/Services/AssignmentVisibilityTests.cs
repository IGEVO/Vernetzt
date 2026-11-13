using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Services;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Visibility rules for contact assignments: the assigned user decides per assignment, pending
/// assignments are only visible internally (user, admin/manager, editors of the entity).
/// </summary>
[TestClass]
public class AssignmentVisibilityTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private static readonly AssignmentViewer Anonymous = AssignmentViewer.Anonymous;
    private static readonly AssignmentViewer RegularUser = new(OtherUserId);
    private static readonly AssignmentViewer Owner = new(OwnerId);
    private static readonly AssignmentViewer AdminOrManager = new(OtherUserId, IsAdminOrManager: true);
    private static readonly AssignmentViewer DataReader = new(OtherUserId, IsDataReader: true);
    private static readonly AssignmentViewer Editor = new(OtherUserId, CanEditEntity: true);

    private static ContactAssignment Assignment(VisibilityLevel visibility, bool confirmed) => new()
    {
        UserId = OwnerId,
        Visibility = visibility,
        ConfirmedAt = confirmed ? DateTime.UtcNow : null
    };

    public static IEnumerable<object[]> Matrix()
    {
        // viewer, visibility, confirmed, expected
        foreach (var level in Enum.GetValues<VisibilityLevel>())
        {
            // Pending: only owner, administration and editors
            yield return ["Anonymous", level, false, false];
            yield return ["RegularUser", level, false, false];
            yield return ["DataReader", level, false, false];
            yield return ["Owner", level, false, true];
            yield return ["AdminOrManager", level, false, true];
            yield return ["Editor", level, false, true];

            // Confirmed: owner, administration, editors and DataReaders always
            yield return ["Owner", level, true, true];
            yield return ["AdminOrManager", level, true, true];
            yield return ["Editor", level, true, true];
            yield return ["DataReader", level, true, true];
        }

        // Confirmed: everyone else by level
        yield return ["Anonymous", VisibilityLevel.Public, true, true];
        yield return ["Anonymous", VisibilityLevel.AllUsers, true, false];
        yield return ["Anonymous", VisibilityLevel.Internal, true, false];
        yield return ["RegularUser", VisibilityLevel.Public, true, true];
        yield return ["RegularUser", VisibilityLevel.AllUsers, true, true];
        yield return ["RegularUser", VisibilityLevel.Internal, true, false];
    }

    [TestMethod]
    [DynamicData(nameof(Matrix))]
    public void CanSeeAssignment_FollowsConsentAndVisibility(string viewer, VisibilityLevel visibility, bool confirmed, bool expected)
    {
        var result = AssignmentVisibility.CanSeeAssignment(Assignment(visibility, confirmed), ViewerFor(viewer));

        Assert.AreEqual(expected, result, $"{viewer}, {visibility}, confirmed={confirmed}");
    }

    [TestMethod]
    public void IsPublic_RequiresConsentAndPublicLevel()
    {
        Assert.IsTrue(AssignmentVisibility.IsPublic(Assignment(VisibilityLevel.Public, confirmed: true)));
        Assert.IsFalse(AssignmentVisibility.IsPublic(Assignment(VisibilityLevel.Public, confirmed: false)));
        Assert.IsFalse(AssignmentVisibility.IsPublic(Assignment(VisibilityLevel.AllUsers, confirmed: true)));
    }

    [TestMethod]
    public void CanSeeField_UsesTheStricterOfAssignmentAndField()
    {
        var publicAssignment = Assignment(VisibilityLevel.Public, confirmed: true);
        var allUsersAssignment = Assignment(VisibilityLevel.AllUsers, confirmed: true);

        Assert.IsTrue(AssignmentVisibility.CanSeeField(publicAssignment, VisibilityLevel.Public, Anonymous));
        Assert.IsFalse(AssignmentVisibility.CanSeeField(publicAssignment, VisibilityLevel.AllUsers, Anonymous));
        Assert.IsFalse(AssignmentVisibility.CanSeeField(allUsersAssignment, VisibilityLevel.Public, Anonymous));
        Assert.IsTrue(AssignmentVisibility.CanSeeField(allUsersAssignment, VisibilityLevel.Public, RegularUser));
        Assert.IsFalse(AssignmentVisibility.CanSeeField(allUsersAssignment, VisibilityLevel.Internal, RegularUser));
    }

    [TestMethod]
    public void CanSeeField_PendingAssignment_EditorsSeeFieldsByTheUsersOwnSetting()
    {
        var pending = Assignment(VisibilityLevel.Internal, confirmed: false);

        Assert.IsTrue(AssignmentVisibility.CanSeeField(pending, VisibilityLevel.AllUsers, Editor));
        Assert.IsFalse(AssignmentVisibility.CanSeeField(pending, VisibilityLevel.Internal, Editor));
        Assert.IsFalse(AssignmentVisibility.CanSeeField(pending, VisibilityLevel.Public, RegularUser));
    }

    private static AssignmentViewer ViewerFor(string name) => name switch
    {
        "Anonymous" => Anonymous,
        "RegularUser" => RegularUser,
        "Owner" => Owner,
        "AdminOrManager" => AdminOrManager,
        "DataReader" => DataReader,
        "Editor" => Editor,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };
}
