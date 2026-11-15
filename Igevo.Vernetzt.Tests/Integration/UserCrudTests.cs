using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Integration tests for User CRUD operations through real EF Core repositories.
/// Tests delete and cascading effects on related entities.
/// </summary>
[TestClass]
public class UserCrudTests : IntegrationTestBase
{
    #region Create

    [TestMethod]
    public async Task User_Create_PersistsToDatabase()
    {
        var user = new User
        {
            Email = "newuser@test.local",
            FirstName = "Max",
            LastName = "Mustermann",
            EmailVerified = true
        };
        user = await UserRepository.AddAsync(user);

        Assert.AreNotEqual(Guid.Empty, user.Id);

        var loaded = await UserRepository.GetByIdAsync(user.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("Max", loaded.FirstName);
        Assert.AreEqual("newuser@test.local", loaded.Email);
    }

    [TestMethod]
    public async Task User_GetByEmail_FindsUser()
    {
        var user = new User
        {
            Email = "findme@test.local",
            FirstName = "Find",
            LastName = "Me"
        };
        await UserRepository.AddAsync(user);

        var found = await UserRepository.GetByEmailAsync("findme@test.local");
        Assert.IsNotNull(found);
        Assert.AreEqual("Find", found.FirstName);
    }

    #endregion

    #region Delete

    [TestMethod]
    public async Task User_Delete_RemovesFromDatabase()
    {
        var user = new User
        {
            Email = "deleteme@test.local",
            FirstName = "Delete",
            LastName = "Me"
        };
        user = await UserRepository.AddAsync(user);

        await UserRepository.DeleteAsync(user.Id);

        DbContext.ChangeTracker.Clear();

        var loaded = await UserRepository.GetByIdAsync(user.Id);
        Assert.IsNull(loaded);

        var byEmail = await UserRepository.GetByEmailAsync("deleteme@test.local");
        Assert.IsNull(byEmail);
    }

    [TestMethod]
    public async Task User_Delete_CascadeDeletesContactAssignments()
    {
        var club = await SeedClubAsync("Testverein");

        var user = new User
        {
            Email = "cascade@test.local",
            FirstName = "Cascade",
            LastName = "Test"
        };
        user = await UserRepository.AddAsync(user);

        var assignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id,
            AssignedAt = DateTime.UtcNow
        };
        DbContext.ContactAssignments.Add(assignment);
        await DbContext.SaveChangesAsync();

        // Delete the user
        await UserRepository.DeleteAsync(user.Id);

        // Assignment should also be deleted (cascade)
        DbContext.ChangeTracker.Clear();
        var loadedAssignment = await DbContext.ContactAssignments.FindAsync(assignment.Id);
        Assert.IsNull(loadedAssignment);
    }

    #endregion

    #region User with Claims

    [TestMethod]
    public async Task User_WithClaims_PersistsAndLoads()
    {
        var user = new User
        {
            Email = "claims@test.local",
            FirstName = "Claims",
            LastName = "User",
            Claims = [UserClaim.SystemAdmin, UserClaim.Manager]
        };
        user = await UserRepository.AddAsync(user);

        DbContext.ChangeTracker.Clear();
        var loaded = await UserRepository.GetByIdAsync(user.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(2, loaded.Claims.Count);
        Assert.IsTrue(loaded.Claims.Contains(UserClaim.SystemAdmin));
        Assert.IsTrue(loaded.Claims.Contains(UserClaim.Manager));
    }

    #endregion

    #region User + ContactAssignment Scenarios

    [TestMethod]
    public async Task User_WithMultipleAssignments_AllAccessible()
    {
        var club1 = await SeedClubAsync("Verein 1");
        var club2 = await SeedClubAsync("Verein 2");
        var position = await SeedContactPositionAsync("Trainer");

        var user = new User
        {
            Email = "multi@test.local",
            FirstName = "Multi",
            LastName = "Assign"
        };
        user = await UserRepository.AddAsync(user);

        DbContext.ContactAssignments.Add(new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club1.Id,
            ContactPositionId = position.Id,
            AssignedAt = DateTime.UtcNow
        });
        DbContext.ContactAssignments.Add(new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club2.Id,
            AssignedAt = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();

        // Reload club with assignments
        DbContext.ChangeTracker.Clear();
        var loadedClub1 = await ClubRepository.GetByIdAsync(club1.Id);
        Assert.IsNotNull(loadedClub1);
        Assert.AreEqual(1, loadedClub1.ContactAssignments.Count);
        Assert.AreEqual(user.Id, loadedClub1.ContactAssignments.First().UserId);

        var loadedClub2 = await ClubRepository.GetByIdAsync(club2.Id);
        Assert.IsNotNull(loadedClub2);
        Assert.AreEqual(1, loadedClub2.ContactAssignments.Count);
    }

    [TestMethod]
    public async Task Club_Delete_RemovesFromDatabase()
    {
        var club = await SeedClubAsync("Testverein");

        await ClubRepository.DeleteAsync(club.Id);

        var loaded = await ClubRepository.GetByIdAsync(club.Id);
        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task Department_Delete_RemovesFromDatabase()
    {
        var club = await SeedClubAsync("Testverein");
        var department = await SeedDepartmentAsync(club.Id, "Fußball");

        await DepartmentRepository.DeleteAsync(department.Id);

        var loaded = await DepartmentRepository.GetByIdAsync(department.Id);
        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task Department_Delete_CascadeDeletesContactAssignmentsAndEmailVerificationTokens()
    {
        var club = await SeedClubAsync("Testverein");
        var department = await SeedDepartmentAsync(club.Id, "Fußball");

        var user = await UserRepository.AddAsync(new User
        {
            Email = "dept-cascade@test.local",
            FirstName = "Dept",
            LastName = "Cascade",
            EmailVerified = true
        });

        var assignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DepartmentId = department.Id,
            AssignedAt = DateTime.UtcNow
        };

        var token = new ContactAssignmentEmailVerificationToken
        {
            Id = Guid.NewGuid(),
            ContactAssignmentId = assignment.Id,
            TokenHash = "hash-dept",
            EmailNormalizedSnapshot = "dept-cascade@test.local",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        DbContext.ContactAssignments.Add(assignment);
        DbContext.ContactAssignmentEmailVerificationTokens.Add(token);
        await DbContext.SaveChangesAsync();

        await DepartmentRepository.DeleteAsync(department.Id);

        DbContext.ChangeTracker.Clear();

        var loadedDepartment = await DbContext.Departments.FindAsync(department.Id);
        var loadedAssignment = await DbContext.ContactAssignments.FindAsync(assignment.Id);
        var loadedToken = await DbContext.ContactAssignmentEmailVerificationTokens.FindAsync(token.Id);

        Assert.IsNull(loadedDepartment);
        Assert.IsNull(loadedAssignment);
        Assert.IsNull(loadedToken);
    }

    [TestMethod]
    public async Task Club_Delete_CascadeDeletesDepartmentsContactAssignmentsAndEmailVerificationTokens()
    {
        var club = await SeedClubAsync("Testverein");
        var department = await SeedDepartmentAsync(club.Id, "Jugend");

        var clubUser = await UserRepository.AddAsync(new User
        {
            Email = "club-cascade@test.local",
            FirstName = "Club",
            LastName = "Cascade",
            EmailVerified = true
        });

        var departmentUser = await UserRepository.AddAsync(new User
        {
            Email = "dept-under-club@test.local",
            FirstName = "Dept",
            LastName = "UnderClub",
            EmailVerified = true
        });

        var clubAssignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = clubUser.Id,
            ClubId = club.Id,
            AssignedAt = DateTime.UtcNow
        };

        var departmentAssignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = departmentUser.Id,
            DepartmentId = department.Id,
            AssignedAt = DateTime.UtcNow
        };

        var clubToken = new ContactAssignmentEmailVerificationToken
        {
            Id = Guid.NewGuid(),
            ContactAssignmentId = clubAssignment.Id,
            TokenHash = "hash-club",
            EmailNormalizedSnapshot = "club-cascade@test.local",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var departmentToken = new ContactAssignmentEmailVerificationToken
        {
            Id = Guid.NewGuid(),
            ContactAssignmentId = departmentAssignment.Id,
            TokenHash = "hash-dept",
            EmailNormalizedSnapshot = "dept-under-club@test.local",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        DbContext.ContactAssignments.AddRange(clubAssignment, departmentAssignment);
        DbContext.ContactAssignmentEmailVerificationTokens.AddRange(clubToken, departmentToken);
        await DbContext.SaveChangesAsync();

        await ClubRepository.DeleteAsync(club.Id);

        DbContext.ChangeTracker.Clear();

        var loadedClub = await DbContext.Clubs.FindAsync(club.Id);
        var loadedDepartment = await DbContext.Departments.FindAsync(department.Id);
        var loadedClubAssignment = await DbContext.ContactAssignments.FindAsync(clubAssignment.Id);
        var loadedDepartmentAssignment = await DbContext.ContactAssignments.FindAsync(departmentAssignment.Id);
        var loadedClubToken = await DbContext.ContactAssignmentEmailVerificationTokens.FindAsync(clubToken.Id);
        var loadedDepartmentToken = await DbContext.ContactAssignmentEmailVerificationTokens.FindAsync(departmentToken.Id);

        Assert.IsNull(loadedClub);
        Assert.IsNull(loadedDepartment);
        Assert.IsNull(loadedClubAssignment);
        Assert.IsNull(loadedDepartmentAssignment);
        Assert.IsNull(loadedClubToken);
        Assert.IsNull(loadedDepartmentToken);
    }

    #endregion
}
