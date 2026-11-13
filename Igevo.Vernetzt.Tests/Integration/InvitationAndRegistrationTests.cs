using Igevo.Vernetzt.Core.DTOs.Invitations;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Integration tests for the invitation and registration flow through real EF Core repositories.
/// Tests the full AcceptInvitationAsync flow including contact assignment creation.
/// </summary>
[TestClass]
public class InvitationAndRegistrationTests : IntegrationTestBase
{
    #region Invite User

    [TestMethod]
    public async Task InviteUser_PlainInvitation_CreatesToken()
    {
        var dto = new InviteUserDto { Email = "newuser@test.local" };

        var response = await UserService.InviteUserAsync(AdminUser.Id, dto);

        Assert.IsTrue(response.Success);
        Assert.IsFalse(response.IsExistingUser);

        // Verify token was created in database
        var token = await InvitationTokenRepository.GetValidByEmailAsync("newuser@test.local");
        Assert.IsNotNull(token);
        Assert.AreEqual("newuser@test.local", token.Email);
        Assert.IsFalse(token.IsUsed);
        Assert.IsFalse(token.IsRevoked);
        Assert.IsNull(token.ClubId);
        Assert.IsNull(token.DepartmentId);
    }

    [TestMethod]
    public async Task InviteUser_WithClubPrelink_StoresClubId()
    {
        var club = await SeedClubAsync("Testverein");
        var dto = new InviteUserDto
        {
            Email = "newuser@test.local",
            ClubId = club.Id
        };

        var response = await UserService.InviteUserAsync(AdminUser.Id, dto);
        Assert.IsTrue(response.Success);

        var token = await InvitationTokenRepository.GetValidByEmailAsync("newuser@test.local");
        Assert.IsNotNull(token);
        Assert.AreEqual(club.Id, token.ClubId);
        Assert.IsNull(token.DepartmentId);
    }

    [TestMethod]
    public async Task InviteUser_WithClubPrelinkAndCanEdit_StoresCanEditFlag()
    {
        var club = await SeedClubAsync("Testverein");
        var dto = new InviteUserDto
        {
            Email = "newuser@test.local",
            ClubId = club.Id,
            CanEdit = true
        };

        var response = await UserService.InviteUserAsync(AdminUser.Id, dto);
        Assert.IsTrue(response.Success);

        var token = await InvitationTokenRepository.GetValidByEmailAsync("newuser@test.local");
        Assert.IsNotNull(token);
        Assert.AreEqual(club.Id, token.ClubId);
        Assert.IsTrue(token.CanEdit);
    }

    [TestMethod]
    public async Task InviteUser_WithClubAndPosition_StoresBoth()
    {
        var club = await SeedClubAsync("Testverein");
        var position = await SeedContactPositionAsync("Vorstand");
        var dto = new InviteUserDto
        {
            Email = "newuser@test.local",
            ClubId = club.Id,
            ContactPositionId = position.Id
        };

        var response = await UserService.InviteUserAsync(AdminUser.Id, dto);
        Assert.IsTrue(response.Success);

        var token = await InvitationTokenRepository.GetValidByEmailAsync("newuser@test.local");
        Assert.IsNotNull(token);
        Assert.AreEqual(club.Id, token.ClubId);
        Assert.AreEqual(position.Id, token.ContactPositionId);
    }

    [TestMethod]
    public async Task InviteUser_WithDepartmentPrelink_StoresDepartmentId()
    {
        var club = await SeedClubAsync("Testverein");
        var department = await SeedDepartmentAsync(club.Id, "Fußball");
        var dto = new InviteUserDto
        {
            Email = "newuser@test.local",
            DepartmentId = department.Id
        };

        var response = await UserService.InviteUserAsync(AdminUser.Id, dto);
        Assert.IsTrue(response.Success);

        var token = await InvitationTokenRepository.GetValidByEmailAsync("newuser@test.local");
        Assert.IsNotNull(token);
        Assert.IsNull(token.ClubId);
        Assert.AreEqual(department.Id, token.DepartmentId);
    }

    [TestMethod]
    public async Task InviteUser_MultipleInvitations_AllStoredSeparately()
    {
        var club1 = await SeedClubAsync("Verein 1");
        var club2 = await SeedClubAsync("Verein 2");
        var position = await SeedContactPositionAsync("Trainer");

        await UserService.InviteUserAsync(AdminUser.Id, new InviteUserDto
        {
            Email = "newuser@test.local",
            ClubId = club1.Id,
            ContactPositionId = position.Id
        });
        await UserService.InviteUserAsync(AdminUser.Id, new InviteUserDto
        {
            Email = "newuser@test.local",
            ClubId = club2.Id
        });

        var allTokens = await InvitationTokenRepository.GetAllValidByEmailAsync("newuser@test.local");
        Assert.AreEqual(2, allTokens.Count);
    }

    #endregion

    #region Accept Invitation (Registration)

    [TestMethod]
    public async Task AcceptInvitation_PlainInvitation_CreatesUserWithoutAssignment()
    {
        var invitation = await SeedInvitationAsync("newuser@test.local");

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);

        Assert.IsTrue(response.Success);

        // Verify user was created
        var user = await UserRepository.GetByEmailAsync("newuser@test.local");
        Assert.IsNotNull(user);
        Assert.AreEqual("Max", user.FirstName);
        Assert.AreEqual("Mustermann", user.LastName);
        Assert.IsTrue(user.EmailVerified);

        // Verify invitation was marked as used
        DbContext.ChangeTracker.Clear();
        var usedToken = await DbContext.InvitationTokens.FirstOrDefaultAsync(t => t.Id == invitation.Id);
        Assert.IsNotNull(usedToken);
        Assert.IsTrue(usedToken.IsUsed);
        Assert.IsNotNull(usedToken.UsedAt);
    }

    [TestMethod]
    public async Task AcceptInvitation_WithClubPrelink_CreatesUserAndAssignment()
    {
        var club = await SeedClubAsync("Testverein");
        var invitation = await SeedInvitationAsync("newuser@test.local", clubId: club.Id);

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsTrue(response.Success);

        // Verify user was created
        var user = await UserRepository.GetByEmailAsync("newuser@test.local");
        Assert.IsNotNull(user);

        // Verify contact assignment was created
        DbContext.ChangeTracker.Clear();
        var assignments = await DbContext.ContactAssignments
            .Where(ca => ca.UserId == user.Id)
            .ToListAsync();
        Assert.AreEqual(1, assignments.Count);
        Assert.AreEqual(club.Id, assignments[0].ClubId);
        Assert.IsNull(assignments[0].DepartmentId);
    }

    [TestMethod]
    public async Task AcceptInvitation_WithChosenVisibility_CreatesConfirmedAssignment()
    {
        var club = await SeedClubAsync("Sichtbarkeitsverein");
        var invitation = await SeedInvitationAsync("visible@test.local", clubId: club.Id);

        var response = await UserService.AcceptInvitationAsync(new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Vera",
            LastName = "Visible",
            AcceptedPrivacyPolicy = true,
            AssignmentVisibilities = new() { [invitation.Id] = VisibilityLevel.Public }
        });
        Assert.IsTrue(response.Success);

        DbContext.ChangeTracker.Clear();
        var user = await UserRepository.GetByEmailAsync("visible@test.local");
        var assignment = await DbContext.ContactAssignments.SingleAsync(ca => ca.UserId == user!.Id);
        // Completing the registration is the consent; no separate confirmation needed
        Assert.IsNotNull(assignment.ConfirmedAt);
        Assert.AreEqual(VisibilityLevel.Public, assignment.Visibility);
        Assert.AreEqual(invitation.InvitedById, assignment.AssignedById);
    }

    [TestMethod]
    public async Task AcceptInvitation_WithoutChosenVisibility_DefaultsToInternal()
    {
        var club = await SeedClubAsync("Stiller Verein");
        var invitation = await SeedInvitationAsync("quiet@test.local", clubId: club.Id);

        var response = await UserService.AcceptInvitationAsync(new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Quentin",
            LastName = "Quiet",
            AcceptedPrivacyPolicy = true
        });
        Assert.IsTrue(response.Success);

        DbContext.ChangeTracker.Clear();
        var user = await UserRepository.GetByEmailAsync("quiet@test.local");
        var assignment = await DbContext.ContactAssignments.SingleAsync(ca => ca.UserId == user!.Id);
        Assert.AreEqual(VisibilityLevel.Internal, assignment.Visibility);
    }

    [TestMethod]
    public async Task AcceptInvitation_WithGroupPrelink_CreatesAssignmentAtGroup()
    {
        var club = await SeedClubAsync("TSV");
        var department = await SeedDepartmentAsync(club.Id, "Handball");
        var group = await DepartmentRepository.AddAsync(new Department
        {
            Name = "Bambini",
            ClubId = club.Id,
            ParentDepartmentId = department.Id
        });
        var invitation = await SeedInvitationAsync("trainer@test.local", departmentId: group.Id, canEdit: true);

        var details = await UserService.GetInvitationDetailsAsync(invitation.Token);
        Assert.IsNotNull(details);
        var pending = details.PendingAssignments.Single();
        Assert.AreEqual("Bambini", pending.DepartmentName);
        Assert.AreEqual("Handball", pending.ParentDepartmentName);
        Assert.AreEqual("TSV", pending.ClubName);

        var response = await UserService.AcceptInvitationAsync(new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Tina",
            LastName = "Trainerin",
            AcceptedPrivacyPolicy = true
        });
        Assert.IsTrue(response.Success);

        var user = await UserRepository.GetByEmailAsync("trainer@test.local");
        Assert.IsNotNull(user);
        DbContext.ChangeTracker.Clear();
        var assignment = await DbContext.ContactAssignments.SingleAsync(ca => ca.UserId == user.Id);
        Assert.AreEqual(group.Id, assignment.DepartmentId);
        Assert.IsTrue(assignment.CanEdit);
    }

    [TestMethod]
    public async Task AcceptInvitation_WithClubPrelinkAndCanEdit_CreatesEditableAssignment()
    {
        var club = await SeedClubAsync("Testverein");
        var invitation = await SeedInvitationAsync("newuser@test.local", clubId: club.Id, canEdit: true);

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsTrue(response.Success);

        var user = await UserRepository.GetByEmailAsync("newuser@test.local");
        Assert.IsNotNull(user);

        DbContext.ChangeTracker.Clear();
        var assignments = await DbContext.ContactAssignments
            .Where(ca => ca.UserId == user.Id)
            .ToListAsync();
        Assert.AreEqual(1, assignments.Count);
        Assert.AreEqual(club.Id, assignments[0].ClubId);
        Assert.IsTrue(assignments[0].CanEdit);
    }

    [TestMethod]
    public async Task AcceptInvitation_WithClubAndPosition_CreatesAssignmentWithPosition()
    {
        var club = await SeedClubAsync("Testverein");
        var position = await SeedContactPositionAsync("Vorstand");
        var invitation = await SeedInvitationAsync("newuser@test.local",
            clubId: club.Id, contactPositionId: position.Id);

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsTrue(response.Success);

        var user = await UserRepository.GetByEmailAsync("newuser@test.local");
        Assert.IsNotNull(user);

        DbContext.ChangeTracker.Clear();
        var assignments = await DbContext.ContactAssignments
            .Where(ca => ca.UserId == user.Id)
            .ToListAsync();
        Assert.AreEqual(1, assignments.Count);
        Assert.AreEqual(club.Id, assignments[0].ClubId);
        Assert.AreEqual(position.Id, assignments[0].ContactPositionId);
    }

    [TestMethod]
    public async Task AcceptInvitation_WithDepartmentPrelink_CreatesAssignment()
    {
        var club = await SeedClubAsync("Testverein");
        var department = await SeedDepartmentAsync(club.Id, "Fußball");
        var invitation = await SeedInvitationAsync("newuser@test.local", departmentId: department.Id);

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsTrue(response.Success);

        var user = await UserRepository.GetByEmailAsync("newuser@test.local");
        Assert.IsNotNull(user);

        DbContext.ChangeTracker.Clear();
        var assignments = await DbContext.ContactAssignments
            .Where(ca => ca.UserId == user.Id)
            .ToListAsync();
        Assert.AreEqual(1, assignments.Count);
        Assert.AreEqual(department.Id, assignments[0].DepartmentId);
        Assert.IsNull(assignments[0].ClubId);
    }

    [TestMethod]
    public async Task AcceptInvitation_MultipleInvitations_ConsolidatesAllAssignments()
    {
        var club1 = await SeedClubAsync("Verein 1");
        var club2 = await SeedClubAsync("Verein 2");
        var position = await SeedContactPositionAsync("Trainer");

        var invitation1 = await SeedInvitationAsync("newuser@test.local",
            clubId: club1.Id, contactPositionId: position.Id);
        var invitation2 = await SeedInvitationAsync("newuser@test.local",
            clubId: club2.Id);

        var dto = new AcceptInvitationDto
        {
            Token = invitation1.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsTrue(response.Success);

        var user = await UserRepository.GetByEmailAsync("newuser@test.local");
        Assert.IsNotNull(user);

        // Both invitations should result in assignments
        DbContext.ChangeTracker.Clear();
        var assignments = await DbContext.ContactAssignments
            .Where(ca => ca.UserId == user.Id)
            .OrderBy(ca => ca.ClubId)
            .ToListAsync();
        Assert.AreEqual(2, assignments.Count);

        // Both invitations should be marked as used
        var usedTokens = await DbContext.InvitationTokens
            .Where(t => t.Email == "newuser@test.local")
            .ToListAsync();
        Assert.IsTrue(usedTokens.All(t => t.IsUsed));
    }

    [TestMethod]
    public async Task AcceptInvitation_MultipleInvitationsSameClub_CreatesOnlyOneAssignment()
    {
        var club = await SeedClubAsync("Testverein");
        var pos1 = await SeedContactPositionAsync("Vorstand");
        var pos2 = await SeedContactPositionAsync("Trainer");

        var invitation1 = await SeedInvitationAsync("newuser@test.local",
            clubId: club.Id, contactPositionId: pos1.Id, canEdit: false);
        // Second invitation for same club
        var invitation2 = await SeedInvitationAsync("newuser@test.local",
            clubId: club.Id, contactPositionId: pos2.Id, canEdit: true);

        var dto = new AcceptInvitationDto
        {
            Token = invitation1.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsTrue(response.Success);

        // Only one assignment for the same club (duplicate prevention)
        DbContext.ChangeTracker.Clear();
        var user = await UserRepository.GetByEmailAsync("newuser@test.local");
        Assert.IsNotNull(user);
        var assignments = await DbContext.ContactAssignments
            .Where(ca => ca.UserId == user.Id && ca.ClubId == club.Id)
            .ToListAsync();
        Assert.AreEqual(1, assignments.Count);
        Assert.IsTrue(assignments[0].CanEdit);

        // But both invitations should be marked as used
        var usedTokens = await DbContext.InvitationTokens
            .Where(t => t.Email == "newuser@test.local")
            .ToListAsync();
        Assert.IsTrue(usedTokens.All(t => t.IsUsed));
    }

    [TestMethod]
    public async Task AcceptInvitation_InvalidToken_ReturnsFalse()
    {
        var dto = new AcceptInvitationDto
        {
            Token = "nonexistent-token",
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsFalse(response.Success);
    }

    [TestMethod]
    public async Task AcceptInvitation_ExpiredToken_ReturnsFalse()
    {
        var token = new InvitationToken
        {
            Id = Guid.NewGuid(),
            Email = "newuser@test.local",
            Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            InvitedById = AdminUser.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-8),
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // Expired
        };
        await InvitationTokenRepository.AddAsync(token);

        var dto = new AcceptInvitationDto
        {
            Token = token.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsFalse(response.Success);
    }

    [TestMethod]
    public async Task AcceptInvitation_AlreadyUsedToken_ReturnsFalse()
    {
        var token = new InvitationToken
        {
            Id = Guid.NewGuid(),
            Email = "newuser@test.local",
            Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            InvitedById = AdminUser.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsUsed = true,
            UsedAt = DateTime.UtcNow
        };
        await InvitationTokenRepository.AddAsync(token);

        var dto = new AcceptInvitationDto
        {
            Token = token.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsFalse(response.Success);
    }

    [TestMethod]
    public async Task AcceptInvitation_PrivacyPolicyNotAccepted_ReturnsFalse()
    {
        var invitation = await SeedInvitationAsync("newuser@test.local");

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = false
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsFalse(response.Success);
        Assert.IsTrue(response.Message.Contains("Datenschutzerklärung"));
    }

    #endregion

    #region Registration with Previously Deleted User

    [TestMethod]
    public async Task AcceptInvitation_PreviouslyDeletedUser_CreatesNewUser()
    {
        // Create and delete a user
        var oldUser = new User
        {
            Email = "returning@test.local",
            FirstName = "Old",
            LastName = "User",
            EmailVerified = true
        };
        oldUser = await UserRepository.AddAsync(oldUser);
        await UserRepository.DeleteAsync(oldUser.Id);

        // Create invitation for the same email
        var invitation = await SeedInvitationAsync("returning@test.local");

        // Accept invitation — should create new user since old one is deleted
        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "New",
            LastName = "User",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsTrue(response.Success);

        // Verify new user exists
        var newUser = await UserRepository.GetByEmailAsync("returning@test.local");
        Assert.IsNotNull(newUser);
        Assert.AreEqual("New", newUser.FirstName);
        Assert.AreNotEqual(oldUser.Id, newUser.Id);
    }

    [TestMethod]
    public async Task AcceptInvitation_PreviouslyDeletedUserWithAssignments_CleanupWorks()
    {
        var club = await SeedClubAsync("Testverein");
        var position = await SeedContactPositionAsync("Vorstand");

        // Create user with assignment
        var oldUser = new User
        {
            Email = "returning@test.local",
            FirstName = "Old",
            LastName = "User",
            EmailVerified = true
        };
        oldUser = await UserRepository.AddAsync(oldUser);

        var oldAssignment = new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(),
            UserId = oldUser.Id,
            ClubId = club.Id,
            ContactPositionId = position.Id,
            AssignedAt = DateTime.UtcNow
        };
        DbContext.ContactAssignments.Add(oldAssignment);
        await DbContext.SaveChangesAsync();

        // Delete the user (cascade deletes assignments)
        await UserRepository.DeleteAsync(oldUser.Id);

        // Create invitation with same club and position
        var invitation = await SeedInvitationAsync("returning@test.local",
            clubId: club.Id, contactPositionId: position.Id);

        // Accept invitation — should create new user with new assignment
        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "New",
            LastName = "User",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsTrue(response.Success);

        // Verify new user with new assignment
        var newUser = await UserRepository.GetByEmailAsync("returning@test.local");
        Assert.IsNotNull(newUser);

        DbContext.ChangeTracker.Clear();
        var assignments = await DbContext.ContactAssignments
            .Where(ca => ca.UserId == newUser.Id)
            .ToListAsync();
        Assert.AreEqual(1, assignments.Count);
        Assert.AreEqual(club.Id, assignments[0].ClubId);
    }

    #endregion

    #region Registration with Deleted ContactPosition

    [TestMethod]
    public async Task AcceptInvitation_ContactPositionDeletedBeforeAcceptance_HandlesGracefully()
    {
        var club = await SeedClubAsync("Testverein");
        var position = await SeedContactPositionAsync("Vorstand");

        // Create invitation with position
        var invitation = await SeedInvitationAsync("newuser@test.local",
            clubId: club.Id, contactPositionId: position.Id);

        // Delete the position BEFORE acceptance
        await ContactPositionRepository.DeleteAsync(position.Id);

        // Accept invitation — should still work, position is SetNull
        DbContext.ChangeTracker.Clear();
        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsTrue(response.Success);

        // Verify user was created
        var user = await UserRepository.GetByEmailAsync("newuser@test.local");
        Assert.IsNotNull(user);

        // Assignment should exist but with null ContactPositionId
        DbContext.ChangeTracker.Clear();
        var assignments = await DbContext.ContactAssignments
            .Where(ca => ca.UserId == user.Id)
            .ToListAsync();
        Assert.AreEqual(1, assignments.Count);
        Assert.AreEqual(club.Id, assignments[0].ClubId);
        Assert.IsNull(assignments[0].ContactPositionId);
    }

    #endregion

    #region Revoke Invitations

    [TestMethod]
    public async Task RevokeInvitation_ValidToken_MarksAsRevoked()
    {
        var invitation = await SeedInvitationAsync("newuser@test.local");

        var result = await UserService.RevokeInvitationAsync(invitation.Token);
        Assert.IsTrue(result);

        // Verify revoked
        DbContext.ChangeTracker.Clear();
        var loaded = await DbContext.InvitationTokens.FindAsync(invitation.Id);
        Assert.IsNotNull(loaded);
        Assert.IsTrue(loaded.IsRevoked);
    }

    [TestMethod]
    public async Task AcceptInvitation_RevokedToken_ReturnsFalse()
    {
        var invitation = await SeedInvitationAsync("newuser@test.local");
        await UserService.RevokeInvitationAsync(invitation.Token);

        var dto = new AcceptInvitationDto
        {
            Token = invitation.Token,
            FirstName = "Max",
            LastName = "Mustermann",
            AcceptedPrivacyPolicy = true
        };

        var response = await UserService.AcceptInvitationAsync(dto);
        Assert.IsFalse(response.Success);
    }

    #endregion

    #region Role/office address (context email)

    [TestMethod]
    public async Task InviteUser_WithContextEmail_StoresAddressOnInvitation()
    {
        var club = await SeedClubAsync("Testverein");

        var response = await UserService.InviteUserAsync(AdminUser.Id, new InviteUserDto
        {
            Email = "max@privat.test",
            ClubId = club.Id,
            ContextEmail = " vorstand@verein.test "
        });

        Assert.IsTrue(response.Success);
        var token = await InvitationTokenRepository.GetValidByEmailAsync("max@privat.test");
        Assert.IsNotNull(token);
        Assert.AreEqual("vorstand@verein.test", token.ContextEmail);
    }

    [TestMethod]
    public async Task InviteUser_ContextEmailWithoutAssignment_IsRejected()
    {
        var response = await UserService.InviteUserAsync(AdminUser.Id, new InviteUserDto
        {
            Email = "max@privat.test",
            ContextEmail = "vorstand@verein.test"
        });

        Assert.IsFalse(response.Success);
        Assert.IsNull(await InvitationTokenRepository.GetValidByEmailAsync("max@privat.test"));
    }

    [TestMethod]
    public async Task InviteUser_ContextEmailEqualsLoginEmail_IsRejected()
    {
        var club = await SeedClubAsync("Testverein");

        var response = await UserService.InviteUserAsync(AdminUser.Id, new InviteUserDto
        {
            Email = "max@privat.test",
            ClubId = club.Id,
            ContextEmail = "MAX@privat.test"
        });

        Assert.IsFalse(response.Success);
    }

    [TestMethod]
    public async Task InviteUser_ContextEmailIsLoginOfExistingUser_IsRejected()
    {
        var club = await SeedClubAsync("Testverein");

        var response = await UserService.InviteUserAsync(AdminUser.Id, new InviteUserDto
        {
            Email = "max@privat.test",
            ClubId = club.Id,
            ContextEmail = AdminUser.Email
        });

        Assert.IsFalse(response.Success);
    }

    [TestMethod]
    public async Task GetInvitationDetails_ReturnsInvitationIdAndSuggestedContextEmail()
    {
        var club = await SeedClubAsync("Testverein");
        var invitation = await SeedInvitationAsync("max@privat.test", clubId: club.Id);
        invitation.ContextEmail = "vorstand@verein.test";
        await InvitationTokenRepository.UpdateAsync(invitation);

        var details = await UserService.GetInvitationDetailsAsync(invitation.Token);

        Assert.IsNotNull(details);
        Assert.HasCount(1, details.PendingAssignments);
        Assert.AreEqual(invitation.Id, details.PendingAssignments[0].InvitationId);
        Assert.AreEqual("vorstand@verein.test", details.PendingAssignments[0].ContextEmail);
    }

    [TestMethod]
    public async Task AcceptInvitation_WithSuggestedContextEmail_SetsUnverifiedAddressAndSendsVerification()
    {
        var club = await SeedClubAsync("Testverein");
        var invitation = await SeedInvitationAsync("max@privat.test", clubId: club.Id);
        invitation.ContextEmail = "Vorstand@Verein.test";
        await InvitationTokenRepository.UpdateAsync(invitation);

        var response = await UserService.AcceptInvitationAsync(NewAcceptDto(invitation.Token));

        Assert.IsTrue(response.Success);
        var assignment = await LoadSingleAssignmentAsync("max@privat.test");
        Assert.AreEqual("Vorstand@Verein.test", assignment.ContextEmail);
        Assert.AreEqual("vorstand@verein.test", assignment.ContextEmailNormalized);
        Assert.IsFalse(assignment.ContextEmailVerified);
        EmailServiceMock.Verify(e => e.SendContactAssignmentEmailVerificationAsync(
            "Vorstand@Verein.test",
            It.Is<string>(url => url.Contains("/api/contact-assignment-emails/verify?token=")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AcceptInvitation_InviteeChangesContextEmail_UsesNewAddress()
    {
        var club = await SeedClubAsync("Testverein");
        var invitation = await SeedInvitationAsync("max@privat.test", clubId: club.Id);
        invitation.ContextEmail = "vorstand@verein.test";
        await InvitationTokenRepository.UpdateAsync(invitation);

        var dto = NewAcceptDto(invitation.Token);
        dto.AssignmentContextEmails = new Dictionary<Guid, string?> { [invitation.Id] = "kasse@verein.test" };
        var response = await UserService.AcceptInvitationAsync(dto);

        Assert.IsTrue(response.Success);
        var assignment = await LoadSingleAssignmentAsync("max@privat.test");
        Assert.AreEqual("kasse@verein.test", assignment.ContextEmail);
    }

    [TestMethod]
    public async Task AcceptInvitation_InviteeRemovesContextEmail_LeavesAssignmentWithoutAddress()
    {
        var club = await SeedClubAsync("Testverein");
        var invitation = await SeedInvitationAsync("max@privat.test", clubId: club.Id);
        invitation.ContextEmail = "vorstand@verein.test";
        await InvitationTokenRepository.UpdateAsync(invitation);

        var dto = NewAcceptDto(invitation.Token);
        dto.AssignmentContextEmails = new Dictionary<Guid, string?> { [invitation.Id] = "" };
        var response = await UserService.AcceptInvitationAsync(dto);

        Assert.IsTrue(response.Success);
        var assignment = await LoadSingleAssignmentAsync("max@privat.test");
        Assert.IsNull(assignment.ContextEmail);
        EmailServiceMock.Verify(e => e.SendContactAssignmentEmailVerificationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AcceptInvitation_ContextEmailEqualsLoginEmail_FailsWithoutCreatingUser()
    {
        var club = await SeedClubAsync("Testverein");
        var invitation = await SeedInvitationAsync("max@privat.test", clubId: club.Id);

        var dto = NewAcceptDto(invitation.Token);
        dto.AssignmentContextEmails = new Dictionary<Guid, string?> { [invitation.Id] = "max@privat.test" };
        var response = await UserService.AcceptInvitationAsync(dto);

        Assert.IsFalse(response.Success);
        Assert.IsNull(await UserRepository.GetByEmailAsync("max@privat.test"));
    }

    private static AcceptInvitationDto NewAcceptDto(string token) => new()
    {
        Token = token,
        FirstName = "Max",
        LastName = "Mustermann",
        AcceptedPrivacyPolicy = true
    };

    private async Task<ContactAssignment> LoadSingleAssignmentAsync(string email)
    {
        var user = await UserRepository.GetByEmailAsync(email);
        Assert.IsNotNull(user);
        DbContext.ChangeTracker.Clear();
        return await DbContext.ContactAssignments.SingleAsync(ca => ca.UserId == user.Id);
    }

    #endregion
}
