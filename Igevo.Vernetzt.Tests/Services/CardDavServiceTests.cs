using Igevo.Vernetzt.Core.DTOs.CardDav;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services.CardDav;
using Igevo.Vernetzt.Tests.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class CardDavServiceTests : IntegrationTestBase
{
    private CardDavService _service = null!;
    private MemoryCache _cache = null!;
    private User _regularUser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://vernetzt.test/" })
            .Build();

        _service = new CardDavService(
            DbContext,
            UserService,
            new Mock<IAvatarService>().Object,
            new Mock<IImageProcessingService>().Object,
            AuditLogRepository,
            _cache,
            configuration,
            new Mock<ILogger<CardDavService>>().Object);

        _regularUser = await UserRepository.AddAsync(new User
        {
            Email = "regular@test.local",
            FirstName = "Regula",
            LastName = "Normal",
            EmailVerified = true
        });
    }

    [TestCleanup]
    public void Cleanup() => _cache.Dispose();

    // ------------------------------------------------------------ access

    [TestMethod]
    public async Task CreateAccess_ReturnsPasswordOnceAndStoresOnlyHash()
    {
        var created = await _service.CreateAccessAsync(_regularUser.Id);

        Assert.IsFalse(string.IsNullOrWhiteSpace(created.Password));
        Assert.IsTrue(created.Status.HasAccess);
        Assert.AreEqual("regular@test.local", created.Status.Username);
        Assert.AreEqual("https://vernetzt.test/carddav/", created.Status.ServerUrl);

        var stored = await DbContext.CardDavTokens.SingleAsync();
        Assert.AreNotEqual(created.Password, stored.TokenHash);
        Assert.IsFalse(stored.TokenHash.Contains(created.Password, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Authenticate_WithEmailAndPassword_ReturnsUserId()
    {
        var created = await _service.CreateAccessAsync(_regularUser.Id);

        var userId = await _service.AuthenticateAsync(" Regular@Test.local ", created.Password);

        Assert.AreEqual(_regularUser.Id, userId);
        Assert.IsNotNull((await DbContext.CardDavTokens.AsNoTracking().SingleAsync()).LastUsedAt);
    }

    [TestMethod]
    public async Task Authenticate_WrongPasswordOrForeignEmail_ReturnsNull()
    {
        var created = await _service.CreateAccessAsync(_regularUser.Id);

        Assert.IsNull(await _service.AuthenticateAsync("regular@test.local", "wrong"));
        Assert.IsNull(await _service.AuthenticateAsync(AdminUser.Email, created.Password));
        Assert.IsNull(await _service.AuthenticateAsync("", created.Password));
    }

    [TestMethod]
    public async Task CreateAccess_Again_RevokesPreviousPassword()
    {
        var first = await _service.CreateAccessAsync(_regularUser.Id);
        var second = await _service.CreateAccessAsync(_regularUser.Id);

        Assert.IsNull(await _service.AuthenticateAsync(_regularUser.Email, first.Password));
        Assert.AreEqual(_regularUser.Id, await _service.AuthenticateAsync(_regularUser.Email, second.Password));
    }

    [TestMethod]
    public async Task RevokeAccess_PasswordStopsWorking()
    {
        var created = await _service.CreateAccessAsync(_regularUser.Id);

        Assert.IsTrue(await _service.RevokeAccessAsync(_regularUser.Id));
        Assert.IsFalse(await _service.RevokeAccessAsync(_regularUser.Id));
        Assert.IsNull(await _service.AuthenticateAsync(_regularUser.Email, created.Password));
        Assert.IsFalse((await _service.GetStatusAsync(_regularUser.Id)).HasAccess);
    }

    // ------------------------------------------------------------ contacts

    [TestMethod]
    public async Task AddressBook_ContainsOnlyContactsWithEmailOrPhone()
    {
        // Users always have an email; hide it to get a contact without any reachable data.
        var hidden = await UserRepository.AddAsync(new User
        {
            Email = "hidden@test.local", FirstName = "Hanna", LastName = "Hidden",
            EmailVisibility = VisibilityLevel.Internal
        });
        var phoneOnly = await UserRepository.AddAsync(new User
        {
            Email = "phone@test.local", FirstName = "Paul", LastName = "Phone",
            EmailVisibility = VisibilityLevel.Internal, PhoneNumber = "+49 30 123456"
        });

        var book = await _service.GetAddressBookAsync(_regularUser.Id);

        Assert.IsNull(book.Find($"{hidden.Id}.vcf"));
        var phoneCard = book.Find($"{phoneOnly.Id}.vcf");
        Assert.IsNotNull(phoneCard);
        StringAssert.Contains(phoneCard!.Content, "TEL;TYPE=VOICE:+49 30 123456");
        Assert.IsFalse(phoneCard.Content.Contains("phone@test.local", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AddressBook_AdminSeesInternalEmails()
    {
        var hidden = await UserRepository.AddAsync(new User
        {
            Email = "hidden@test.local", FirstName = "Hanna", LastName = "Hidden",
            EmailVisibility = VisibilityLevel.Internal
        });

        var book = await _service.GetAddressBookAsync(AdminUser.Id);

        var card = book.Find($"{hidden.Id}.vcf");
        Assert.IsNotNull(card);
        StringAssert.Contains(card!.Content, "EMAIL;TYPE=INTERNET,PREF:hidden@test.local");
    }

    [TestMethod]
    public async Task ContactCard_ListsAllClubsInOrgAndAssignmentsInTitleAndNote()
    {
        var position = await SeedContactPositionAsync("Kassenwart");
        var tsv = await SeedClubAsync("TSV Musterstadt");
        var sv = await SeedClubAsync("SV Beispiel");
        var football = await SeedDepartmentAsync(tsv.Id, "Fußball");
        DbContext.ContactAssignments.AddRange(
            new ContactAssignment { ConfirmedAt = DateTime.UtcNow, Id = Guid.NewGuid(), UserId = _regularUser.Id, ClubId = sv.Id, ContactPositionId = position.Id },
            new ContactAssignment { ConfirmedAt = DateTime.UtcNow, Id = Guid.NewGuid(), UserId = _regularUser.Id, DepartmentId = football.Id });
        await DbContext.SaveChangesAsync();

        var book = await _service.GetAddressBookAsync(_regularUser.Id);
        var card = book.Find($"{_regularUser.Id}.vcf")!.Content;

        StringAssert.Contains(card, "FN:Regula Normal");
        StringAssert.Contains(card, "N:Normal;Regula;;;");
        StringAssert.Contains(card, @"ORG:SV Beispiel\, TSV Musterstadt");
        StringAssert.Contains(card, "TITLE:Kassenwart (SV Beispiel)");
        StringAssert.Contains(card, @"NOTE:SV Beispiel – Kassenwart\nTSV Musterstadt › Fußball");
        StringAssert.Contains(card, @"CATEGORIES:Vernetzt,SV Beispiel,TSV Musterstadt");
    }

    [TestMethod]
    public async Task ContactCard_MultipleClubs_UsesAbbreviationsInTitle()
    {
        var chair = await SeedContactPositionAsync("Vorstand");
        var coach = await SeedContactPositionAsync("Trainer");
        var tvm = await ClubRepository.AddAsync(new Club { Name = "Turnverein Musterstadt", ShortName = "TVM" });
        var sv = await SeedClubAsync("SV Beispiel");
        var football = await DepartmentRepository.AddAsync(new Department { Name = "Fußball", ShortName = "FB", ClubId = sv.Id });
        DbContext.ContactAssignments.AddRange(
            new ContactAssignment { ConfirmedAt = DateTime.UtcNow, Id = Guid.NewGuid(), UserId = _regularUser.Id, ClubId = tvm.Id, ContactPositionId = chair.Id },
            new ContactAssignment { ConfirmedAt = DateTime.UtcNow, Id = Guid.NewGuid(), UserId = _regularUser.Id, DepartmentId = football.Id, ContactPositionId = coach.Id });
        await DbContext.SaveChangesAsync();

        var card = (await _service.GetAddressBookAsync(_regularUser.Id)).Find($"{_regularUser.Id}.vcf")!.Content;

        // Abbreviated club (TVM), name without abbreviation (SV Beispiel), abbreviated department (FB).
        StringAssert.Contains(card, @"TITLE:Trainer (SV Beispiel › FB)\, Vorstand (TVM)");
        // ORG and NOTE keep the full names.
        StringAssert.Contains(card, @"ORG:SV Beispiel\, Turnverein Musterstadt");
        StringAssert.Contains(card, "NOTE:SV Beispiel › Fußball – Trainer");
    }

    [TestMethod]
    public async Task ContactCard_SingleClub_TitleHasOnlyPositions()
    {
        var chair = await SeedContactPositionAsync("Vorstand");
        var coach = await SeedContactPositionAsync("Trainer");
        var tvm = await ClubRepository.AddAsync(new Club { Name = "Turnverein Musterstadt", ShortName = "TVM" });
        var football = await SeedDepartmentAsync(tvm.Id, "Fußball");
        var handball = await SeedDepartmentAsync(tvm.Id, "Handball");
        DbContext.ContactAssignments.AddRange(
            new ContactAssignment { ConfirmedAt = DateTime.UtcNow, Id = Guid.NewGuid(), UserId = _regularUser.Id, ClubId = tvm.Id, ContactPositionId = chair.Id },
            new ContactAssignment { ConfirmedAt = DateTime.UtcNow, Id = Guid.NewGuid(), UserId = _regularUser.Id, DepartmentId = football.Id, ContactPositionId = coach.Id },
            new ContactAssignment { ConfirmedAt = DateTime.UtcNow, Id = Guid.NewGuid(), UserId = _regularUser.Id, DepartmentId = handball.Id, ContactPositionId = coach.Id });
        await DbContext.SaveChangesAsync();

        var card = (await _service.GetAddressBookAsync(_regularUser.Id)).Find($"{_regularUser.Id}.vcf")!.Content;

        StringAssert.Contains(card, "ORG:Turnverein Musterstadt\r\n");
        // Club first, then departments; the same position is listed once.
        StringAssert.Contains(card, "TITLE:Vorstand\\, Trainer\r\n");
    }

    [TestMethod]
    public async Task ContactCard_IncludesVerifiedOfficeEmail()
    {
        var club = await SeedClubAsync("TSV Musterstadt");
        DbContext.ContactAssignments.Add(new ContactAssignment
        {
            ConfirmedAt = DateTime.UtcNow,
            Id = Guid.NewGuid(), UserId = _regularUser.Id, ClubId = club.Id,
            ContextEmail = "vorstand@tsv.test", ContextEmailNormalized = "vorstand@tsv.test", ContextEmailVerified = true
        });
        await DbContext.SaveChangesAsync();

        var card = (await _service.GetAddressBookAsync(AdminUser.Id)).Find($"{_regularUser.Id}.vcf")!.Content;

        StringAssert.Contains(card, "EMAIL;TYPE=INTERNET,WORK:vorstand@tsv.test");
    }

    // ------------------------------------------------------------ organizations

    [TestMethod]
    public async Task OrganizationCards_OnlyWhenEnabled()
    {
        var club = await SeedClubAsync("TSV Musterstadt");

        var withoutOrgs = await _service.GetAddressBookAsync(_regularUser.Id);
        Assert.IsNull(withoutOrgs.Find($"org-{club.Id}.vcf"));

        await _service.UpdateSettingsAsync(_regularUser.Id, new UpdateCardDavSettingsDto { IncludeOrganizations = true });
        var withOrgs = await _service.GetAddressBookAsync(_regularUser.Id);

        Assert.IsNotNull(withOrgs.Find($"org-{club.Id}.vcf"));
        Assert.AreNotEqual(withoutOrgs.CTag, withOrgs.CTag);
    }

    [TestMethod]
    public async Task OrganizationCards_InheritDataAndUseHierarchicalOrg()
    {
        var club = await ClubRepository.AddAsync(new Club
        {
            Name = "TSV Musterstadt", Street = "Sportweg 1", PostalCode = "12345", City = "Musterstadt",
            Email = "info@tsv.test", WebsiteUrl = "https://tsv.test"
        });
        var football = await DepartmentRepository.AddAsync(new Department
        {
            Name = "Fußball", ClubId = club.Id, Email = "fussball@tsv.test"
        });
        var youth = await DepartmentRepository.AddAsync(new Department
        {
            Name = "E-Jugend", ClubId = club.Id, ParentDepartmentId = football.Id
        });
        await _service.UpdateSettingsAsync(_regularUser.Id, new UpdateCardDavSettingsDto { IncludeOrganizations = true });

        var book = await _service.GetAddressBookAsync(_regularUser.Id);

        var clubCard = book.Find($"org-{club.Id}.vcf")!.Content;
        StringAssert.Contains(clubCard, "ORG:TSV Musterstadt\r\n");
        StringAssert.Contains(clubCard, "X-ABShowAs:COMPANY");
        StringAssert.Contains(clubCard, "ADR;TYPE=WORK:;;Sportweg 1;Musterstadt;;12345;");
        StringAssert.Contains(clubCard, "CATEGORIES:Vernetzt,Verein");

        var groupCard = book.Find($"org-{youth.Id}.vcf")!.Content;
        StringAssert.Contains(groupCard, "FN:TSV Musterstadt – Fußball – E-Jugend");
        StringAssert.Contains(groupCard, "ORG:TSV Musterstadt – Fußball – E-Jugend\r\n");
        // Email from the department, address and website from the club.
        StringAssert.Contains(groupCard, "EMAIL;TYPE=INTERNET,WORK:fussball@tsv.test");
        StringAssert.Contains(groupCard, "ADR;TYPE=WORK:;;Sportweg 1;Musterstadt;;12345;");
        StringAssert.Contains(groupCard, "URL:https://tsv.test");
        StringAssert.Contains(groupCard, "CATEGORIES:Vernetzt,Gruppe");
    }

    [TestMethod]
    public async Task OrganizationCards_WithoutAnyContactData_AreSkipped()
    {
        var club = await ClubRepository.AddAsync(new Club { Name = "Leerer Verein" });
        var department = await SeedDepartmentAsync(club.Id, "Leere Abteilung");
        await _service.UpdateSettingsAsync(_regularUser.Id, new UpdateCardDavSettingsDto { IncludeOrganizations = true });

        var book = await _service.GetAddressBookAsync(_regularUser.Id);

        Assert.IsNull(book.Find($"org-{club.Id}.vcf"));
        Assert.IsNull(book.Find($"org-{department.Id}.vcf"));
    }

    [TestMethod]
    public async Task AddressBook_ETagChangesWhenContactChanges()
    {
        var before = (await _service.GetAddressBookAsync(AdminUser.Id)).Find($"{_regularUser.Id}.vcf")!;

        _regularUser.PhoneNumber = "+49 1234";
        await UserRepository.UpdateAsync(_regularUser);
        _cache.Compact(1.0);

        var after = (await _service.GetAddressBookAsync(AdminUser.Id)).Find($"{_regularUser.Id}.vcf")!;
        Assert.AreNotEqual(before.ETag, after.ETag);
    }
}
