using Igevo.Vernetzt.Core.DTOs.Events;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class CalendarFeedServiceTests
{
    private Mock<ICalendarFeedTokenRepository> _tokenRepositoryMock = null!;
    private Mock<IEventRepository> _eventRepositoryMock = null!;
    private Mock<IAuthorizationService> _authorizationServiceMock = null!;
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IAuditLogRepository> _auditLogRepositoryMock = null!;
    private Mock<ILogger<CalendarFeedService>> _loggerMock = null!;
    private CalendarFeedService _service = null!;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _clubId = Guid.NewGuid();

    [TestInitialize]
    public void Setup()
    {
        _tokenRepositoryMock = new Mock<ICalendarFeedTokenRepository>();
        _eventRepositoryMock = new Mock<IEventRepository>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _auditLogRepositoryMock = new Mock<IAuditLogRepository>();
        _loggerMock = new Mock<ILogger<CalendarFeedService>>();

        _authorizationServiceMock
            .Setup(a => a.CanEditClubAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _authorizationServiceMock
            .Setup(a => a.CanEditDepartmentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = _userId,
                Email = "test@example.com",
                Claims = [UserClaim.SystemAdmin]
            });

        _tokenRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<CalendarFeedToken>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarFeedToken t, CancellationToken _) =>
            {
                t.Id = Guid.NewGuid();
                t.CreatedAt = DateTime.UtcNow;
                t.UpdatedAt = DateTime.UtcNow;
                return t;
            });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://vernetzt.test"
            })
            .Build();

        var dataProtectionProvider = DataProtectionProvider.Create("Igevo.Vernetzt.Tests");

        _service = new CalendarFeedService(
            _tokenRepositoryMock.Object,
            _eventRepositoryMock.Object,
            _authorizationServiceMock.Object,
            _userRepositoryMock.Object,
            _auditLogRepositoryMock.Object,
            dataProtectionProvider,
            config,
            _loggerMock.Object);
    }

    [TestMethod]
    public async Task CreateTokenAsync_GlobalScope_BuildsAbsoluteUrl()
    {
        var dto = new CreateCalendarFeedTokenDto
        {
            Scope = CalendarFeedScope.Global,
            HighlightsOnly = true
        };

        var result = await _service.CreateTokenAsync(_userId, dto);

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Token);
        Assert.AreEqual(CalendarFeedScope.Global, result.Token.Scope);
        Assert.IsTrue(result.Token.HighlightsOnly);
        Assert.IsTrue(result.FeedUrl.StartsWith("https://vernetzt.test/api/calendar-feeds/", StringComparison.Ordinal));
        Assert.IsTrue(result.FeedUrl.EndsWith(".ics", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetTokensAsync_ClubWithoutEditRights_ReturnsOnlyActiveTokens()
    {
        _authorizationServiceMock
            .Setup(a => a.CanEditClubAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tokenRepositoryMock
            .Setup(r => r.GetByClubAsync(_clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarFeedToken>
            {
                new() { Id = Guid.NewGuid(), Scope = CalendarFeedScope.Club, ClubId = _clubId },
                new() { Id = Guid.NewGuid(), Scope = CalendarFeedScope.Club, ClubId = _clubId, IsRevoked = true }
            });

        var result = await _service.GetTokensAsync(_userId, clubId: _clubId);

        Assert.AreEqual(1, result.Count);
        Assert.IsFalse(result[0].IsRevoked);
    }

    [TestMethod]
    public async Task GetTokensAsync_ClubWithEditRights_ReturnsRevokedTokensToo()
    {
        _tokenRepositoryMock
            .Setup(r => r.GetByClubAsync(_clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarFeedToken>
            {
                new() { Id = Guid.NewGuid(), Scope = CalendarFeedScope.Club, ClubId = _clubId },
                new() { Id = Guid.NewGuid(), Scope = CalendarFeedScope.Club, ClubId = _clubId, IsRevoked = true }
            });

        var result = await _service.GetTokensAsync(_userId, clubId: _clubId);

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public async Task GetTokensAsync_GlobalAsRegularUser_ReturnsActiveTokensInsteadOfThrowing()
    {
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _userId, Email = "test@example.com", Claims = [] });
        _tokenRepositoryMock
            .Setup(r => r.GetGlobalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarFeedToken>
            {
                new() { Id = Guid.NewGuid(), Scope = CalendarFeedScope.Global },
                new() { Id = Guid.NewGuid(), Scope = CalendarFeedScope.Global, IsRevoked = true }
            });

        var result = await _service.GetTokensAsync(_userId, globalOnly: true);

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public async Task CreateTokenAsync_ClubScopeWithoutPermission_ThrowsUnauthorizedAccessException()
    {
        _authorizationServiceMock
            .Setup(a => a.CanEditClubAsync(_userId, _clubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var dto = new CreateCalendarFeedTokenDto
        {
            Scope = CalendarFeedScope.Club,
            ClubId = _clubId,
            IncludeDepartments = true
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateTokenAsync(_userId, dto));
    }

    [TestMethod]
    public async Task RotateTokenAsync_ExistingClubToken_RevokesOldAndCreatesNewToken()
    {
        var tokenId = Guid.NewGuid();
        var existing = new CalendarFeedToken
        {
            Id = tokenId,
            Scope = CalendarFeedScope.Club,
            ClubId = _clubId,
            IncludeDepartments = true,
            TokenHash = "old-hash",
            IsRevoked = false
        };

        _tokenRepositoryMock
            .Setup(r => r.GetByIdAsync(tokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _service.RotateTokenAsync(_userId, tokenId);

        Assert.IsNotNull(result);
        Assert.IsTrue(existing.IsRevoked);
        Assert.IsNotNull(existing.RevokedAt);

        _tokenRepositoryMock.Verify(
            r => r.UpdateAsync(It.Is<CalendarFeedToken>(t => t.Id == tokenId && t.IsRevoked), It.IsAny<CancellationToken>()),
            Times.Once);

        _tokenRepositoryMock.Verify(
            r => r.AddAsync(It.Is<CalendarFeedToken>(t => t.Scope == CalendarFeedScope.Club && t.TokenHash != "old-hash"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task RevokeTokenAsync_NonExistingToken_ReturnsFalse()
    {
        var tokenId = Guid.NewGuid();
        _tokenRepositoryMock
            .Setup(r => r.GetByIdAsync(tokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarFeedToken?)null);

        var result = await _service.RevokeTokenAsync(_userId, tokenId);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task GetFeedByTokenAsync_UnknownToken_ReturnsNull()
    {
        _tokenRepositoryMock
            .Setup(r => r.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarFeedToken?)null);

        var result = await _service.GetFeedByTokenAsync("does-not-exist");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetFeedByTokenAsync_ClubFeed_ContainsAllEvents()
    {
        var rawToken = "club-feed-token";
        var tokenHash = EmailAddressHelper.Sha256(rawToken);

        var token = new CalendarFeedToken
        {
            Id = Guid.NewGuid(),
            Scope = CalendarFeedScope.Club,
            ClubId = _clubId,
            IncludeDepartments = true,
            IsRevoked = false,
            TokenHash = tokenHash
        };

        _tokenRepositoryMock
            .Setup(r => r.GetActiveByTokenHashAsync(tokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        var firstEvent = new Event
        {
            Id = Guid.NewGuid(),
            Title = "Jahreshauptversammlung",
            StartUtc = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc),
            ClubId = _clubId
        };

        var secondEvent = new Event
        {
            Id = Guid.NewGuid(),
            Title = "Public Event",
            StartUtc = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 8, 5, 11, 0, 0, DateTimeKind.Utc),
            ClubId = _clubId
        };

        _eventRepositoryMock
            .Setup(r => r.GetByClubAsync(_clubId, true, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([firstEvent, secondEvent]);

        var result = await _service.GetFeedByTokenAsync(rawToken);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Content.Contains("Public Event", StringComparison.Ordinal));
        Assert.IsTrue(result.Content.Contains("Jahreshauptversammlung", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetFeedByTokenAsync_ClubToken_UsesIncludeDepartmentsFlagAndClubFileName()
    {
        var rawToken = "club-token";
        var tokenHash = EmailAddressHelper.Sha256(rawToken);

        var token = new CalendarFeedToken
        {
            Id = Guid.NewGuid(),
            Scope = CalendarFeedScope.Club,
            ClubId = _clubId,
            IncludeDepartments = false,
            IsRevoked = false,
            TokenHash = tokenHash
        };

        _tokenRepositoryMock
            .Setup(r => r.GetActiveByTokenHashAsync(tokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _eventRepositoryMock
            .Setup(r => r.GetByClubAsync(_clubId, false, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Event>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Title = "Club Event",
                    StartUtc = new DateTime(2026, 8, 9, 9, 0, 0, DateTimeKind.Utc),
                    EndUtc = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc),
                    ClubId = _clubId
                }
            });

        var result = await _service.GetFeedByTokenAsync(rawToken);

        Assert.IsNotNull(result);
        Assert.AreEqual("club.ics", result.FileName);

        _eventRepositoryMock.Verify(
            r => r.GetByClubAsync(_clubId, false, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
