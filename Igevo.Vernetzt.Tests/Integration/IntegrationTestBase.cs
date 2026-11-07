using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Repositories;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Igevo.Vernetzt.Tests.TestInfrastructure;

namespace Igevo.Vernetzt.Tests.Integration;

/// <summary>
/// Base class for integration tests that use an in-memory SQLite database.
/// Sets up real repositories and services against a shared DbContext.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected VernetztDbContext DbContext { get; private set; } = null!;

    // Repositories (real EF Core implementations)
    protected IUserRepository UserRepository { get; private set; } = null!;
    protected IClubRepository ClubRepository { get; private set; } = null!;
    protected IClubTypeRepository ClubTypeRepository { get; private set; } = null!;
    protected IContactPositionRepository ContactPositionRepository { get; private set; } = null!;
    protected IDepartmentRepository DepartmentRepository { get; private set; } = null!;
    protected IAuditLogRepository AuditLogRepository { get; private set; } = null!;
    protected IInvitationTokenRepository InvitationTokenRepository { get; private set; } = null!;

    // Services (real implementations with real repos, mocked email)
    protected IUserService UserService { get; private set; } = null!;

    // Mocks (only for services that need external dependencies)
    protected Mock<IEmailService> EmailServiceMock { get; private set; } = null!;
    protected Mock<IAuthorizationService> AuthorizationServiceMock { get; private set; } = null!;

    // Seeded admin user for operations that require requestingUserId
    protected User AdminUser { get; private set; } = null!;

    [TestInitialize]
    public async Task BaseSetup()
    {
        // Own in-memory SQLite database per test to ensure isolation
        DbContext = SqliteTestDatabase.CreateContext();

        // Create real repositories
        UserRepository = new UserRepository(DbContext, new Mock<ILogger<UserRepository>>().Object);
        ClubRepository = new ClubRepository(DbContext, new Mock<ILogger<ClubRepository>>().Object);
        ClubTypeRepository = new ClubTypeRepository(DbContext, new Mock<ILogger<ClubTypeRepository>>().Object);
        ContactPositionRepository = new ContactPositionRepository(DbContext, new Mock<ILogger<ContactPositionRepository>>().Object);
        DepartmentRepository = new DepartmentRepository(DbContext, new Mock<ILogger<DepartmentRepository>>().Object);
        AuditLogRepository = new AuditLogRepository(DbContext, new Mock<ILogger<AuditLogRepository>>().Object);
        InvitationTokenRepository = new InvitationTokenRepository(DbContext);

        // Mock external services
        EmailServiceMock = new Mock<IEmailService>();
        EmailServiceMock.Setup(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        AuthorizationServiceMock = new Mock<IAuthorizationService>();
        AuthorizationServiceMock.Setup(a => a.IsSystemAdminAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        AuthorizationServiceMock.Setup(a => a.CanEditClubAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        AuthorizationServiceMock.Setup(a => a.CanEditDepartmentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Create AppSettings
        var appSettings = Options.Create(new AppSettings
        {
            SystemAdminEmail = "admin@test.local",
            InvitationTokenExpirationDays = 7
        });

        var appSettingsServiceMock = new Mock<IAppSettingsService>();
        appSettingsServiceMock.Setup(s => s.GetValuesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                ["PrivacyPolicyUrl"] = "https://test.local/privacy",
                ["ContactName"] = "Test Admin",
                ["ContactEmail"] = "admin@test.local"
            });

        // Create UserService with real repos
        UserService = new UserService(
            UserRepository,
            ClubRepository,
            DepartmentRepository,
            ContactPositionRepository,
            AuthorizationServiceMock.Object,
            AuditLogRepository,
            EmailServiceMock.Object,
            InvitationTokenRepository,
            appSettingsServiceMock.Object,
            DbContext,
            appSettings,
            new Mock<ILogger<UserService>>().Object);

        // Seed admin user
        AdminUser = new User
        {
            Email = "admin@test.local",
            FirstName = "Admin",
            LastName = "User",
            EmailVerified = true,
            Claims = [UserClaim.SystemAdmin]
        };
        AdminUser = await UserRepository.AddAsync(AdminUser);
    }

    [TestCleanup]
    public void BaseCleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        DbContext?.Dispose();
        GC.SuppressFinalize(this);
    }

    // --- Helper methods ---

    protected async Task<ClubType> SeedClubTypeAsync(string name = "Sportverein")
    {
        var clubType = new ClubType { Name = name };
        return await ClubTypeRepository.AddAsync(clubType);
    }

    protected async Task<ContactPosition> SeedContactPositionAsync(string name = "Vorstand")
    {
        var position = new ContactPosition { Name = name };
        return await ContactPositionRepository.AddAsync(position);
    }

    protected async Task<Club> SeedClubAsync(string name = "Testverein", ClubType? clubType = null)
    {
        var club = new Club
        {
            Name = name,
            ClubTypeId = clubType?.Id,
            City = "Teststadt"
        };
        return await ClubRepository.AddAsync(club);
    }

    protected async Task<Department> SeedDepartmentAsync(Guid clubId, string name = "Testabteilung")
    {
        var department = new Department
        {
            Name = name,
            ClubId = clubId
        };
        return await DepartmentRepository.AddAsync(department);
    }

    protected async Task<InvitationToken> SeedInvitationAsync(
        string email,
        Guid? clubId = null,
        Guid? departmentId = null,
        Guid? contactPositionId = null,
        bool canEdit = false)
    {
        var token = new InvitationToken
        {
            Id = Guid.NewGuid(),
            Email = email,
            Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            InvitedById = AdminUser.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            ClubId = clubId,
            DepartmentId = departmentId,
            ContactPositionId = contactPositionId,
            CanEdit = canEdit
        };
        return await InvitationTokenRepository.AddAsync(token);
    }
}
