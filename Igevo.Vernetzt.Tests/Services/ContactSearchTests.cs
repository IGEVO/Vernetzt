using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Igevo.Vernetzt.Tests.TestInfrastructure;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class ContactSearchTests
{
    private Mock<IUserRepository> _userRepo = null!;
    private Mock<IClubRepository> _clubRepo = null!;
    private Mock<IDepartmentRepository> _departmentRepo = null!;
    private Mock<IContactPositionRepository> _positionRepo = null!;
    private Mock<IAuthorizationService> _authService = null!;
    private Mock<IAuditLogRepository> _auditRepo = null!;
    private Mock<IEmailService> _emailService = null!;
    private Mock<IInvitationTokenRepository> _invitationRepo = null!;
    private Mock<IAppSettingsService> _appSettingsService = null!;
    private VernetztDbContext _dbContext = null!;
    private UserService _service = null!;

    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _regularUserId = Guid.NewGuid();
    private readonly Guid _contact1Id = Guid.NewGuid();
    private readonly Guid _contact2Id = Guid.NewGuid();
    private readonly Guid _contact3Id = Guid.NewGuid();

    private Club _clubA = null!;
    private Club _clubB = null!;
    private Department _deptA1 = null!;
    private ContactPosition _positionVorstand = null!;
    private ContactPosition _positionTrainer = null!;

    [TestInitialize]
    public void Setup()
    {
        _userRepo = new Mock<IUserRepository>();
        _clubRepo = new Mock<IClubRepository>();
        _departmentRepo = new Mock<IDepartmentRepository>();
        _positionRepo = new Mock<IContactPositionRepository>();
        _authService = new Mock<IAuthorizationService>();
        _auditRepo = new Mock<IAuditLogRepository>();
        _emailService = new Mock<IEmailService>();
        _invitationRepo = new Mock<IInvitationTokenRepository>();
        _appSettingsService = new Mock<IAppSettingsService>();
        _dbContext = SqliteTestDatabase.CreateContext();

        var appSettings = Options.Create(new AppSettings { SystemAdminEmail = "admin@test.local" });

        _service = new UserService(
            _userRepo.Object,
            _clubRepo.Object,
            _departmentRepo.Object,
            _positionRepo.Object,
            _authService.Object,
            _auditRepo.Object,
            _emailService.Object,
            _invitationRepo.Object,
            _appSettingsService.Object,
            _dbContext,
            appSettings,
            new Mock<ILogger<UserService>>().Object);

        // Setup positions
        _positionVorstand = new ContactPosition { Id = Guid.NewGuid(), Name = "Vorstand" };
        _positionTrainer = new ContactPosition { Id = Guid.NewGuid(), Name = "Trainer" };

        // Setup clubs
        _clubA = new Club
        {
            Id = Guid.NewGuid(),
            Name = "FC Teststadt",
            City = "Teststadt",
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = _contact1Id, ClubId = default, ContactPositionId = _positionVorstand.Id, AssignedAt = DateTime.UtcNow, ConfirmedAt = DateTime.UtcNow, Visibility = VisibilityLevel.AllUsers }
            }
        };
        _clubA.ContactAssignments.First().ClubId = _clubA.Id;

        _clubB = new Club
        {
            Id = Guid.NewGuid(),
            Name = "TV Musterstadt",
            City = "Musterstadt",
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = _contact2Id, ClubId = default, ContactPositionId = _positionTrainer.Id, AssignedAt = DateTime.UtcNow, ConfirmedAt = DateTime.UtcNow, Visibility = VisibilityLevel.AllUsers }
            }
        };
        _clubB.ContactAssignments.First().ClubId = _clubB.Id;

        // Setup departments
        _deptA1 = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Fußball",
            ClubId = _clubA.Id,
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = _contact3Id, DepartmentId = default, ContactPositionId = _positionTrainer.Id, AssignedAt = DateTime.UtcNow, ConfirmedAt = DateTime.UtcNow, Visibility = VisibilityLevel.AllUsers }
            }
        };
        _deptA1.ContactAssignments.First().DepartmentId = _deptA1.Id;

        // Setup users
        var adminUser = new User
        {
            Id = _adminUserId,
            Email = "admin@test.local",
            FirstName = "Admin",
            LastName = "User",
            Claims = [UserClaim.SystemAdmin],
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers
        };

        var contact1 = new User
        {
            Id = _contact1Id,
            Email = "max@test.local",
            FirstName = "Max",
            LastName = "Müller",
            PhoneNumber = "0171-1234567",
            Claims = [],
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers
        };

        var contact2 = new User
        {
            Id = _contact2Id,
            Email = "anna@test.local",
            FirstName = "Anna",
            LastName = "Schmidt",
            Claims = [],
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers
        };

        var contact3 = new User
        {
            Id = _contact3Id,
            Email = "peter@test.local",
            FirstName = "Peter",
            LastName = "Weber",
            Claims = [],
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers
        };

        var regularUser = new User
        {
            Id = _regularUserId,
            Email = "regular@test.local",
            FirstName = "Regular",
            LastName = "User",
            Claims = [],
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers
        };

        var allUsers = new List<User> { adminUser, contact1, contact2, contact3, regularUser };

        // Wire up mocks
        _userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(allUsers);
        _userRepo.Setup(r => r.GetByIdAsync(_adminUserId, It.IsAny<CancellationToken>())).ReturnsAsync(adminUser);
        _userRepo.Setup(r => r.GetByIdAsync(_regularUserId, It.IsAny<CancellationToken>())).ReturnsAsync(regularUser);
        _clubRepo.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Club> { _clubA, _clubB });
        _departmentRepo.Setup(r => r.GetByClubIdAsync(_clubA.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department> { _deptA1 });
        _departmentRepo.Setup(r => r.GetByClubIdAsync(_clubB.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        _positionRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ContactPosition> { _positionVorstand, _positionTrainer });
        _authService.Setup(s => s.IsSystemAdminAsync(_adminUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authService.Setup(s => s.IsSystemAdminAsync(_regularUserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
    }

    [TestMethod]
    public async Task SearchContacts_NoSearch_ReturnsFirstPage()
    {
        var parameters = new ContactSearchParameters { Page = 1, PageSize = 2 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Items.Count());
        Assert.IsTrue(result.TotalCount >= 3); // Admin sees all users including themselves
    }

    [TestMethod]
    public async Task SearchContacts_Pagination_ReturnsCorrectPage()
    {
        var parameters = new ContactSearchParameters { Page = 1, PageSize = 2 };

        var page1 = await _service.SearchContactsAsync(_adminUserId, parameters);

        parameters.Page = 2;
        var page2 = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.AreEqual(2, page1.Items.Count());
        Assert.IsTrue(page2.Items.Count() >= 1);
        // No overlap between pages
        Assert.IsFalse(page1.Items.Select(c => c.Id).Intersect(page2.Items.Select(c => c.Id)).Any());
    }

    [TestMethod]
    public async Task SearchContacts_SearchByFirstName_FiltersCorrectly()
    {
        var parameters = new ContactSearchParameters { SearchTerm = "Max", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual("Max", result.Items.First().FirstName);
    }

    [TestMethod]
    public async Task SearchContacts_SearchByLastName_FiltersCorrectly()
    {
        var parameters = new ContactSearchParameters { SearchTerm = "Schmidt", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual("Anna", result.Items.First().FirstName);
    }

    [TestMethod]
    public async Task SearchContacts_SearchByClubName_FiltersCorrectly()
    {
        var parameters = new ContactSearchParameters { SearchTerm = "FC Teststadt", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        // Should find contact1 (club member) and contact3 (dept member of same club)
        Assert.IsTrue(result.TotalCount >= 1);
        Assert.IsTrue(result.Items.Any(c => c.Id == _contact1Id));
    }

    [TestMethod]
    public async Task SearchContacts_SearchByDepartmentName_FiltersCorrectly()
    {
        var parameters = new ContactSearchParameters { SearchTerm = "Fußball", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.IsTrue(result.Items.Any(c => c.Id == _contact3Id));
    }

    [TestMethod]
    public async Task SearchContacts_SearchByPositionName_FiltersCorrectly()
    {
        var parameters = new ContactSearchParameters { SearchTerm = "Vorstand", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.IsTrue(result.Items.Any(c => c.Id == _contact1Id));
    }

    [TestMethod]
    public async Task SearchContacts_SearchByParentClubName_FindsDepartmentContact()
    {
        // Search for "FC Teststadt" should find Peter Weber who is only in dept Fußball (under FC Teststadt)
        var parameters = new ContactSearchParameters { SearchTerm = "Teststadt", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.IsTrue(result.Items.Any(c => c.Id == _contact3Id),
            "Department contact should be found when searching parent club name");
    }

    [TestMethod]
    public async Task SearchContacts_CaseInsensitiveSearch_Works()
    {
        var parameters = new ContactSearchParameters { SearchTerm = "müller", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual(_contact1Id, result.Items.First().Id);
    }

    [TestMethod]
    public async Task SearchContacts_NoResults_ReturnsEmptyPage()
    {
        var parameters = new ContactSearchParameters { SearchTerm = "XYZNonExistent", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.AreEqual(0, result.TotalCount);
        Assert.IsFalse(result.Items.Any());
    }

    [TestMethod]
    public async Task SearchContacts_PagedResult_HasCorrectMetadata()
    {
        var parameters = new ContactSearchParameters { Page = 1, PageSize = 2 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.AreEqual(1, result.Page);
        Assert.AreEqual(2, result.PageSize);
        Assert.IsTrue(result.TotalPages >= 2);
        Assert.IsTrue(result.HasNextPage);
        Assert.IsFalse(result.HasPreviousPage);
    }

    [TestMethod]
    public async Task SearchContacts_NormalizesParameters()
    {
        var parameters = new ContactSearchParameters { Page = -1, PageSize = 500 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        // Should normalize Page to 1 and PageSize to 100 (max)
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Page);
        Assert.AreEqual(100, result.PageSize);
    }

    [TestMethod]
    public async Task SearchContacts_SearchByTrainer_FindsMultipleContacts()
    {
        // Both contact2 and contact3 have "Trainer" position
        var parameters = new ContactSearchParameters { SearchTerm = "Trainer", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        Assert.IsTrue(result.Items.Any(c => c.Id == _contact2Id));
        Assert.IsTrue(result.Items.Any(c => c.Id == _contact3Id));
    }

    [TestMethod]
    public async Task SearchContacts_ContactsHaveOrganizations_ClubAssignment()
    {
        // contact1 is assigned to clubA - verify organization is populated
        var parameters = new ContactSearchParameters { SearchTerm = "Max", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        var contact = result.Items.Single(c => c.Id == _contact1Id);
        Assert.IsTrue(contact.Organizations.Count >= 1, "Contact should have at least one organization");
        var clubOrg = contact.Organizations.Single(o => o.Type == OrganizationType.Club);
        Assert.AreEqual(_clubA.Id, clubOrg.OrganizationId);
        Assert.AreEqual("FC Teststadt", clubOrg.OrganizationName);
        Assert.AreEqual("Vorstand", clubOrg.PositionName);
    }

    [TestMethod]
    public async Task SearchContacts_ContactsHaveOrganizations_DepartmentAssignment()
    {
        // contact3 is assigned to deptA1 (Fußball under FC Teststadt)
        var parameters = new ContactSearchParameters { SearchTerm = "Peter", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        var contact = result.Items.Single(c => c.Id == _contact3Id);
        Assert.IsTrue(contact.Organizations.Count >= 1, "Contact should have at least one organization");
        var deptOrg = contact.Organizations.Single(o => o.Type == OrganizationType.Department);
        Assert.AreEqual(_deptA1.Id, deptOrg.OrganizationId);
        Assert.AreEqual("Fußball", deptOrg.OrganizationName);
        Assert.AreEqual("FC Teststadt", deptOrg.ParentName);
        Assert.AreEqual("Trainer", deptOrg.PositionName);
    }

    [TestMethod]
    public async Task SearchContacts_NonAdminUser_SeesAllContacts()
    {
        // Regular user (not admin, no assignments) should still see all contacts
        var parameters = new ContactSearchParameters { Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_regularUserId, parameters);

        // Should see all 4 users (admin, contact1, contact2, contact3) + regularUser itself = 5 total
        Assert.AreEqual(5, result.TotalCount, "Non-admin should see all contacts");
        Assert.IsTrue(result.Items.Any(c => c.Id == _adminUserId));
        Assert.IsTrue(result.Items.Any(c => c.Id == _contact1Id));
        Assert.IsTrue(result.Items.Any(c => c.Id == _contact2Id));
        Assert.IsTrue(result.Items.Any(c => c.Id == _contact3Id));
        Assert.IsTrue(result.Items.Any(c => c.Id == _regularUserId));
    }

    [TestMethod]
    public async Task SearchContacts_NonAdminUser_SeesContactsWithoutAssignments()
    {
        // Admin user has no club/department assignment but should still appear for non-admin
        var parameters = new ContactSearchParameters { SearchTerm = "Admin", Page = 1, PageSize = 20 };

        var result = await _service.SearchContactsAsync(_regularUserId, parameters);

        Assert.AreEqual(1, result.TotalCount, "User without assignments should still be visible");
        Assert.AreEqual(_adminUserId, result.Items.First().Id);
    }

    [TestMethod]
    public async Task SearchContacts_NonAdminUser_EmailHiddenWhenVisibilityInternal()
    {
        // contact1 has AllUsers visibility by default — change to Internal for this test
        // We need a fresh user with Internal email visibility
        var internalUserId = Guid.NewGuid();
        var internalUser = new User
        {
            Id = internalUserId,
            Email = "internal@test.local",
            FirstName = "Internal",
            LastName = "Contact",
            Claims = [],
            EmailVisibility = VisibilityLevel.Internal,
            PhoneVisibility = VisibilityLevel.Internal
        };

        // Re-setup mocks to include the new user
        _userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { internalUser });
        _userRepo.Setup(r => r.GetByIdAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _regularUserId, Email = "regular@test.local", FirstName = "Regular", LastName = "User", Claims = [] });
        _clubRepo.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Club>());

        var parameters = new ContactSearchParameters { Page = 1, PageSize = 20 };
        var result = await _service.SearchContactsAsync(_regularUserId, parameters);

        // The internal user should appear but without email/phone
        var contact = result.Items.FirstOrDefault(c => c.Id == internalUserId);
        Assert.IsNotNull(contact, "User should be visible regardless of email visibility");
        Assert.IsNull(contact.Email, "Email should be hidden for Internal visibility");
        Assert.IsNull(contact.PhoneNumber, "Phone should be hidden for Internal visibility");
    }

    [TestMethod]
    public async Task SearchContacts_AdminUser_SeesEmailEvenWhenInternal()
    {
        var internalUserId = Guid.NewGuid();
        var internalUser = new User
        {
            Id = internalUserId,
            Email = "internal@test.local",
            FirstName = "Internal",
            LastName = "Contact",
            PhoneNumber = "0171-SECRET",
            Claims = [],
            EmailVisibility = VisibilityLevel.Internal,
            PhoneVisibility = VisibilityLevel.Internal
        };

        _userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { internalUser });
        _clubRepo.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Club>());

        var parameters = new ContactSearchParameters { Page = 1, PageSize = 20 };
        var result = await _service.SearchContactsAsync(_adminUserId, parameters);

        var contact = result.Items.FirstOrDefault(c => c.Id == internalUserId);
        Assert.IsNotNull(contact);
        Assert.AreEqual("internal@test.local", contact.Email, "Admin should see email even with Internal visibility");
        Assert.AreEqual("0171-SECRET", contact.PhoneNumber, "Admin should see phone even with Internal visibility");
    }

    [TestMethod]
    public async Task SearchContacts_UserSeesOwnEmailRegardlessOfVisibility()
    {
        // Regular user should see their own email even if set to Internal
        var selfUser = new User
        {
            Id = _regularUserId,
            Email = "regular@test.local",
            FirstName = "Regular",
            LastName = "User",
            PhoneNumber = "0171-PRIVATE",
            Claims = [],
            EmailVisibility = VisibilityLevel.Internal,
            PhoneVisibility = VisibilityLevel.Internal
        };

        _userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { selfUser });
        _userRepo.Setup(r => r.GetByIdAsync(_regularUserId, It.IsAny<CancellationToken>())).ReturnsAsync(selfUser);
        _clubRepo.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Club>());

        var parameters = new ContactSearchParameters { Page = 1, PageSize = 20 };
        var result = await _service.SearchContactsAsync(_regularUserId, parameters);

        var contact = result.Items.Single(c => c.Id == _regularUserId);
        Assert.AreEqual("regular@test.local", contact.Email, "User should see own email even with Internal visibility");
        Assert.AreEqual("0171-PRIVATE", contact.PhoneNumber, "User should see own phone even with Internal visibility");
    }

    [TestMethod]
    public async Task GetAllContacts_NonAdmin_HidesInternalEmail()
    {
        // Verify that GetAllContactsAsync (used by email recipient selector) also respects visibility
        var internalUserId = Guid.NewGuid();
        var internalUser = new User
        {
            Id = internalUserId,
            Email = "hidden@test.local",
            FirstName = "Hidden",
            LastName = "User",
            PhoneNumber = "0171-SECRET",
            Claims = [],
            EmailVisibility = VisibilityLevel.Internal,
            PhoneVisibility = VisibilityLevel.Internal
        };

        _userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { internalUser });
        _userRepo.Setup(r => r.GetByIdAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _regularUserId, Email = "regular@test.local", FirstName = "Regular", LastName = "User", Claims = [] });
        _clubRepo.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Club>());

        var contacts = await _service.GetAllContactsAsync(_regularUserId);

        var contact = contacts.Single(c => c.Id == internalUserId);
        Assert.IsNull(contact.Email, "Non-admin should not see email with Internal visibility via GetAllContactsAsync");
        Assert.IsNull(contact.PhoneNumber, "Non-admin should not see phone with Internal visibility via GetAllContactsAsync");
    }

    [TestMethod]
    public async Task GetAllContacts_NonAdmin_SeesAllUsersEmailWhenVisibilityAllUsers()
    {
        // Verify that emails with AllUsers visibility ARE visible
        var publicUserId = Guid.NewGuid();
        var publicUser = new User
        {
            Id = publicUserId,
            Email = "visible@test.local",
            FirstName = "Visible",
            LastName = "User",
            PhoneNumber = "0171-PUBLIC",
            Claims = [],
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers
        };

        _userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { publicUser });
        _userRepo.Setup(r => r.GetByIdAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _regularUserId, Email = "regular@test.local", FirstName = "Regular", LastName = "User", Claims = [] });
        _clubRepo.Setup(r => r.GetAllWithDetailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Club>());

        var contacts = await _service.GetAllContactsAsync(_regularUserId);

        var contact = contacts.Single(c => c.Id == publicUserId);
        Assert.AreEqual("visible@test.local", contact.Email, "Non-admin should see email with AllUsers visibility");
        Assert.AreEqual("0171-PUBLIC", contact.PhoneNumber, "Non-admin should see phone with AllUsers visibility");
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }
}
