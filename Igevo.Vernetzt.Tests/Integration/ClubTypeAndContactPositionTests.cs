using Igevo.Vernetzt.Core.Entities;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Integration tests for ClubType and ContactPosition CRUD operations through real EF Core repositories.
/// Verifies that hard delete works correctly and cascading effects are handled properly.
/// </summary>
[TestClass]
public class ClubTypeAndContactPositionTests : IntegrationTestBase
{
    #region ClubType CRUD

    [TestMethod]
    public async Task ClubType_Create_PersistsToDatabase()
    {
        var clubType = await SeedClubTypeAsync("Sportverein");

        Assert.AreNotEqual(Guid.Empty, clubType.Id);
        Assert.AreEqual("Sportverein", clubType.Name);

        var loaded = await ClubTypeRepository.GetByIdAsync(clubType.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("Sportverein", loaded.Name);
    }

    [TestMethod]
    public async Task ClubType_Update_PersistsChanges()
    {
        var clubType = await SeedClubTypeAsync("Sportverein");
        clubType.Name = "Kulturverein";
        await ClubTypeRepository.UpdateAsync(clubType);

        var loaded = await ClubTypeRepository.GetByIdAsync(clubType.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("Kulturverein", loaded.Name);
    }

    [TestMethod]
    public async Task ClubType_Delete_HardDeletesFromDatabase()
    {
        var clubType = await SeedClubTypeAsync("Sportverein");
        var id = clubType.Id;

        var result = await ClubTypeRepository.DeleteAsync(id);
        Assert.IsTrue(result);

        // Verify hard delete — entity should be gone completely
        var loaded = await ClubTypeRepository.GetByIdAsync(id);
        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task ClubType_Delete_SetsClubForeignKeyToNull()
    {
        var clubType = await SeedClubTypeAsync("Sportverein");
        var club = await SeedClubAsync("Testverein", clubType);

        // Verify the club has the ClubType
        var loadedClub = await ClubRepository.GetByIdAsync(club.Id);
        Assert.IsNotNull(loadedClub);
        Assert.AreEqual(clubType.Id, loadedClub.ClubTypeId);

        // Delete the ClubType
        await ClubTypeRepository.DeleteAsync(clubType.Id);

        // Reload the club — FK should be set to null (SetNull behavior)
        DbContext.ChangeTracker.Clear();
        loadedClub = await ClubRepository.GetByIdAsync(club.Id);
        Assert.IsNotNull(loadedClub);
        Assert.IsNull(loadedClub.ClubTypeId);
    }

    #endregion

    #region ContactPosition CRUD

    [TestMethod]
    public async Task ContactPosition_Create_PersistsToDatabase()
    {
        var position = await SeedContactPositionAsync("Vorstand");

        Assert.AreNotEqual(Guid.Empty, position.Id);
        Assert.AreEqual("Vorstand", position.Name);

        var loaded = await ContactPositionRepository.GetByIdAsync(position.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("Vorstand", loaded.Name);
    }

    [TestMethod]
    public async Task ContactPosition_Update_PersistsChanges()
    {
        var position = await SeedContactPositionAsync("Vorstand");
        position.Name = "Trainer";
        await ContactPositionRepository.UpdateAsync(position);

        var loaded = await ContactPositionRepository.GetByIdAsync(position.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("Trainer", loaded.Name);
    }

    [TestMethod]
    public async Task ContactPosition_Delete_HardDeletesFromDatabase()
    {
        var position = await SeedContactPositionAsync("Vorstand");
        var id = position.Id;

        var result = await ContactPositionRepository.DeleteAsync(id);
        Assert.IsTrue(result);

        var loaded = await ContactPositionRepository.GetByIdAsync(id);
        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task ContactPosition_Delete_SetsContactAssignmentPositionToNull()
    {
        var position = await SeedContactPositionAsync("Vorstand");
        var club = await SeedClubAsync("Testverein");

        // Create a user and assign them as contact person with the position
        var user = new User
        {
            Email = "contact@test.local",
            FirstName = "Contact",
            LastName = "Person",
            EmailVerified = true
        };
        user = await UserRepository.AddAsync(user);

        var assignment = new ContactAssignment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id,
            ContactPositionId = position.Id,
            AssignedAt = DateTime.UtcNow
        };
        DbContext.ContactAssignments.Add(assignment);
        await DbContext.SaveChangesAsync();

        // Delete the ContactPosition
        await ContactPositionRepository.DeleteAsync(position.Id);

        // Reload assignment — ContactPositionId should be null (SetNull behavior)
        DbContext.ChangeTracker.Clear();
        var loadedAssignment = await DbContext.ContactAssignments.FindAsync(assignment.Id);
        Assert.IsNotNull(loadedAssignment);
        Assert.IsNull(loadedAssignment.ContactPositionId);
    }

    [TestMethod]
    public async Task ContactPosition_Delete_SetsInvitationTokenPositionToNull()
    {
        var position = await SeedContactPositionAsync("Vorstand");
        var club = await SeedClubAsync("Testverein");

        // Create an invitation with a pre-linked contact position
        var invitation = await SeedInvitationAsync(
            "newuser@test.local",
            clubId: club.Id,
            contactPositionId: position.Id);

        // Verify the invitation has the position
        var loaded = await InvitationTokenRepository.GetByTokenAsync(invitation.Token);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(position.Id, loaded.ContactPositionId);

        // Delete the ContactPosition
        await ContactPositionRepository.DeleteAsync(position.Id);

        // Reload invitation — ContactPositionId should be null (SetNull behavior)
        DbContext.ChangeTracker.Clear();
        loaded = await DbContext.InvitationTokens.FindAsync(invitation.Id);
        Assert.IsNotNull(loaded);
        Assert.IsNull(loaded.ContactPositionId);
    }

    #endregion

    #region Multi-Entity Scenarios

    [TestMethod]
    public async Task ClubType_DeleteMultiple_AllHardDeleted()
    {
        var type1 = await SeedClubTypeAsync("Sportverein");
        var type2 = await SeedClubTypeAsync("Kulturverein");
        var type3 = await SeedClubTypeAsync("Förderverein");

        await ClubTypeRepository.DeleteAsync(type1.Id);
        await ClubTypeRepository.DeleteAsync(type2.Id);

        var all = (await ClubTypeRepository.GetAllAsync()).ToList();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("Förderverein", all[0].Name);
    }

    [TestMethod]
    public async Task ContactPosition_DeleteMultiple_AllHardDeleted()
    {
        var pos1 = await SeedContactPositionAsync("Vorstand");
        var pos2 = await SeedContactPositionAsync("Trainer");
        var pos3 = await SeedContactPositionAsync("Kassenwart");

        await ContactPositionRepository.DeleteAsync(pos1.Id);
        await ContactPositionRepository.DeleteAsync(pos2.Id);

        var all = (await ContactPositionRepository.GetAllAsync()).ToList();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("Kassenwart", all[0].Name);
    }

    #endregion
}
