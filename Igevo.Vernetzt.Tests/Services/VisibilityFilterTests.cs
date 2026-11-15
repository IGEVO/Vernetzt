using System.Security.Claims;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Unit tests for visibility filtering in DtoMappingService.
/// </summary>
[TestClass]
public class VisibilityFilterTests
{
    private Mock<IHttpContextAccessor> _httpContextAccessorMock = null!;
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IContactPositionRepository> _contactPositionRepositoryMock = null!;
    private Mock<IAuthorizationService> _authorizationServiceMock = null!;
    private DtoMappingService _service = null!;

    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _regularUserId = Guid.NewGuid();
    private readonly Guid _clubId = Guid.NewGuid();

    [TestInitialize]
    public void Setup()
    {
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _contactPositionRepositoryMock = new Mock<IContactPositionRepository>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();

        _service = new DtoMappingService(
            _httpContextAccessorMock.Object,
            _userRepositoryMock.Object,
            _contactPositionRepositoryMock.Object,
            _authorizationServiceMock.Object);

        SetupTestUsers();
    }

    private void SetupTestUsers()
    {
        // Admin user
        var adminUser = new User
        {
            Id = _adminUserId,
            Email = "admin@test.com",
            FirstName = "Admin",
            LastName = "User",
            PhoneNumber = "123456",
            EmailVisibility = VisibilityLevel.Public,
            PhoneVisibility = VisibilityLevel.Public,
            Claims = new List<UserClaim> { UserClaim.SystemAdmin }
        };

        // Regular user with varying visibility settings
        var regularUser = new User
        {
            Id = _regularUserId,
            Email = "regular@test.com",
            FirstName = "Regular",
            LastName = "User",
            PhoneNumber = "654321",
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.Internal,
            Claims = new List<UserClaim>()
        };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);
    }

    private void SetupHttpContext(Guid? userId)
    {
        var httpContext = new DefaultHttpContext();
        if (userId.HasValue)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.Value.ToString())
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            httpContext.User = new ClaimsPrincipal(identity);
        }
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);
    }

    #region Club Mapping Tests

    [TestMethod]
    public async Task MapClubToDtoAsync_AdminUser_CanEditAndDelete()
    {
        // Arrange
        SetupHttpContext(_adminUserId);
        _authorizationServiceMock.Setup(s => s.CanEditClubAsync(_adminUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _authorizationServiceMock.Setup(s => s.IsSystemAdminAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>()
        };

        // Act
        var result = await _service.MapClubToDtoAsync(club);

        // Assert
        Assert.IsTrue(result.CanEdit);
        Assert.IsTrue(result.CanDelete);
        Assert.IsTrue(result.CanManageContacts);
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_RegularUser_CannotDelete()
    {
        // Arrange
        SetupHttpContext(_regularUserId);
        _authorizationServiceMock.Setup(s => s.CanEditClubAsync(_regularUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Can edit (assigned as contact)
        _authorizationServiceMock.Setup(s => s.IsSystemAdminAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>()
        };

        // Act
        var result = await _service.MapClubToDtoAsync(club);

        // Assert
        Assert.IsTrue(result.CanEdit);
        Assert.IsFalse(result.CanDelete);
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_NoAuthentication_NoPermissions()
    {
        // Arrange
        SetupHttpContext(null);

        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>()
        };

        // Act
        var result = await _service.MapClubToDtoAsync(club);

        // Assert
        Assert.IsFalse(result.CanEdit);
        Assert.IsFalse(result.CanDelete);
        Assert.IsFalse(result.CanManageContacts);
    }

    #endregion

    #region Contact Visibility Tests

    [TestMethod]
    public async Task MapClubToDtoAsync_PublicAssignment_ContactsVisible()
    {
        // Arrange
        SetupHttpContext(null); // Not authenticated
        var contactUserId = Guid.NewGuid();
        var contactUser = new User
        {
            Id = contactUserId,
            Email = "contact@test.com",
            FirstName = "Contact",
            LastName = "Person",
            PhoneNumber = "111222",
            EmailVisibility = VisibilityLevel.Public,
            PhoneVisibility = VisibilityLevel.Public
        };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(contactUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactUser);

        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = contactUserId, ClubId = _clubId, AssignedAt = DateTime.UtcNow, Visibility = VisibilityLevel.Public, ConfirmedAt = DateTime.UtcNow }
            }
        };

        // Act
        var result = await _service.MapClubToDtoAsync(club);

        // Assert
        Assert.AreEqual(1, result.ContactAssignments.Count);
        Assert.AreEqual("Contact", result.ContactAssignments[0].FirstName);
        Assert.AreEqual("contact@test.com", result.ContactAssignments[0].Email);
        Assert.AreEqual("111222", result.ContactAssignments[0].PhoneNumber);
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_AllUsersAssignment_NoAuthNoContacts()
    {
        // Arrange
        SetupHttpContext(null); // Not authenticated
        var contactUserId = Guid.NewGuid();

        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = contactUserId, ClubId = _clubId, AssignedAt = DateTime.UtcNow, Visibility = VisibilityLevel.AllUsers, ConfirmedAt = DateTime.UtcNow }
            }
        };

        // Act
        var result = await _service.MapClubToDtoAsync(club);

        // Assert
        Assert.AreEqual(0, result.ContactAssignments.Count);
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_InternalContactInfo_RegularUserCannotSee()
    {
        // Arrange
        SetupHttpContext(_regularUserId);
        var contactUserId = Guid.NewGuid();
        var contactUser = new User
        {
            Id = contactUserId,
            Email = "internal@test.com",
            FirstName = "Internal",
            LastName = "Contact",
            PhoneNumber = "999888",
            EmailVisibility = VisibilityLevel.Internal, // Only admins/managers
            PhoneVisibility = VisibilityLevel.Internal
        };

        var regularUser = new User
        {
            Id = _regularUserId,
            Email = "regular@test.com",
            FirstName = "Regular",
            LastName = "User",
            Claims = new List<UserClaim>() // No special claims
        };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(contactUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_regularUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularUser);

        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = contactUserId, ClubId = _clubId, AssignedAt = DateTime.UtcNow, Visibility = VisibilityLevel.AllUsers, ConfirmedAt = DateTime.UtcNow }
            }
        };

        // Act
        var result = await _service.MapClubToDtoAsync(club);

        // Assert
        Assert.AreEqual(1, result.ContactAssignments.Count);
        Assert.AreEqual("Internal", result.ContactAssignments[0].FirstName); // Names always visible
        Assert.IsNull(result.ContactAssignments[0].Email); // Internal visibility - not visible
        Assert.IsNull(result.ContactAssignments[0].PhoneNumber); // Internal visibility - not visible
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_InternalContactInfo_AdminCanSee()
    {
        // Arrange
        SetupHttpContext(_adminUserId);
        var contactUserId = Guid.NewGuid();
        var contactUser = new User
        {
            Id = contactUserId,
            Email = "internal@test.com",
            FirstName = "Internal",
            LastName = "Contact",
            PhoneNumber = "999888",
            EmailVisibility = VisibilityLevel.Internal,
            PhoneVisibility = VisibilityLevel.Internal
        };

        var adminUser = new User
        {
            Id = _adminUserId,
            Email = "admin@test.com",
            FirstName = "Admin",
            LastName = "User",
            Claims = new List<UserClaim> { UserClaim.SystemAdmin }
        };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(contactUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactUser);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminUser);
        _authorizationServiceMock.Setup(s => s.CanEditClubAsync(_adminUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _authorizationServiceMock.Setup(s => s.IsSystemAdminAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = contactUserId, ClubId = _clubId, AssignedAt = DateTime.UtcNow, Visibility = VisibilityLevel.AllUsers, ConfirmedAt = DateTime.UtcNow }
            }
        };

        // Act
        var result = await _service.MapClubToDtoAsync(club);

        // Assert
        Assert.AreEqual(1, result.ContactAssignments.Count);
        Assert.AreEqual("internal@test.com", result.ContactAssignments[0].Email); // Admin can see
        Assert.AreEqual("999888", result.ContactAssignments[0].PhoneNumber); // Admin can see
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_PendingAssignment_HiddenFromRegularUser()
    {
        SetupHttpContext(_regularUserId);
        var contactUserId = AddContactUser();
        var club = ClubWith(new ContactAssignment { UserId = contactUserId, ClubId = _clubId, Visibility = VisibilityLevel.Public });

        var result = await _service.MapClubToDtoAsync(club);

        Assert.AreEqual(0, result.ContactAssignments.Count);
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_PendingAssignment_VisibleForEditorAsPending()
    {
        SetupHttpContext(_regularUserId);
        _authorizationServiceMock.Setup(s => s.CanEditClubAsync(_regularUserId, _clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var contactUserId = AddContactUser();
        var club = ClubWith(new ContactAssignment { UserId = contactUserId, ClubId = _clubId });

        var result = await _service.MapClubToDtoAsync(club);

        Assert.AreEqual(1, result.ContactAssignments.Count);
        Assert.IsFalse(result.ContactAssignments[0].IsConfirmed);
        // Personal fields still follow the user's own settings (AllUsers)
        Assert.AreEqual("contact@test.com", result.ContactAssignments[0].Email);
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_PendingAssignment_HiddenFromDataReader()
    {
        var readerId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(readerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = readerId, Email = "reader@test.com", Claims = [UserClaim.DataReader] });
        SetupHttpContext(readerId);
        var contactUserId = AddContactUser();
        var club = ClubWith(
            new ContactAssignment { UserId = contactUserId, ClubId = _clubId },
            new ContactAssignment { UserId = _regularUserId, ClubId = _clubId, Visibility = VisibilityLevel.Internal, ConfirmedAt = DateTime.UtcNow });

        var result = await _service.MapClubToDtoAsync(club);

        // DataReaders see confirmed assignments of every level, but no pending ones
        Assert.AreEqual(1, result.ContactAssignments.Count);
        Assert.AreEqual(_regularUserId, result.ContactAssignments[0].UserId);
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_PendingAssignment_VisibleForAssignedUser()
    {
        var contactUserId = AddContactUser();
        SetupHttpContext(contactUserId);
        var club = ClubWith(new ContactAssignment { UserId = contactUserId, ClubId = _clubId });

        var result = await _service.MapClubToDtoAsync(club);

        Assert.AreEqual(1, result.ContactAssignments.Count);
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_InternalAssignment_HiddenFromRegularUser()
    {
        SetupHttpContext(_regularUserId);
        var contactUserId = AddContactUser();
        var club = ClubWith(new ContactAssignment
        {
            UserId = contactUserId, ClubId = _clubId, Visibility = VisibilityLevel.Internal, ConfirmedAt = DateTime.UtcNow
        });

        var result = await _service.MapClubToDtoAsync(club);

        Assert.AreEqual(0, result.ContactAssignments.Count);
    }

    [TestMethod]
    public async Task MapClubToDtoAsync_ContextEmailFollowsAssignmentVisibility()
    {
        SetupHttpContext(null);
        var contactUserId = AddContactUser();
        var club = ClubWith(new ContactAssignment
        {
            UserId = contactUserId, ClubId = _clubId, Visibility = VisibilityLevel.Public, ConfirmedAt = DateTime.UtcNow,
            ContextEmail = "vorstand@verein.example", ContextEmailVerified = true
        });

        var result = await _service.MapClubToDtoAsync(club);

        Assert.AreEqual(1, result.ContactAssignments.Count);
        Assert.AreEqual("vorstand@verein.example", result.ContactAssignments[0].Email);
        // The personal address (AllUsers) stays hidden on public access
        Assert.AreNotEqual("contact@test.com", result.ContactAssignments[0].Email);
    }

    private Guid AddContactUser()
    {
        var contactUserId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(contactUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = contactUserId,
                Email = "contact@test.com",
                FirstName = "Contact",
                LastName = "Person",
                EmailVisibility = VisibilityLevel.AllUsers,
                PhoneVisibility = VisibilityLevel.AllUsers
            });
        return contactUserId;
    }

    private Club ClubWith(params ContactAssignment[] assignments) =>
        new() { Id = _clubId, Name = "Test Club", ContactAssignments = assignments.ToList() };

    #endregion

    #region Not Found User Tests

    [TestMethod]
    public async Task MapClubToDtoAsync_NotFoundContactPerson_NotIncluded()
    {
        // Arrange
        SetupHttpContext(_adminUserId);
        var missingUserId = Guid.NewGuid();

        _userRepositoryMock.Setup(r => r.GetByIdAsync(missingUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _authorizationServiceMock.Setup(s => s.CanEditClubAsync(_adminUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _authorizationServiceMock.Setup(s => s.IsSystemAdminAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var club = new Club
        {
            Id = _clubId,
            Name = "Test Club",
            ContactAssignments = new List<ContactAssignment>
            {
                new() { UserId = missingUserId, ClubId = _clubId, AssignedAt = DateTime.UtcNow, Visibility = VisibilityLevel.AllUsers, ConfirmedAt = DateTime.UtcNow }
            }
        };

        // Act
        var result = await _service.MapClubToDtoAsync(club);

        // Assert
        Assert.AreEqual(0, result.ContactAssignments.Count); // Not-found users filtered out
    }

    #endregion
}
