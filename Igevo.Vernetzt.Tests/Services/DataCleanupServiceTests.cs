using Igevo.Vernetzt.Core.Configuration;
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

/// <summary>
/// Unit tests for the DataCleanupService (daily cleanup job).
/// </summary>
[TestClass]
public class DataCleanupServiceTests
{
    private Mock<IAuditLogRepository> _auditLogRepositoryMock = null!;
    private Mock<IInvitationTokenRepository> _invitationTokenRepositoryMock = null!;
    private Mock<IEmailRelayTokenRepository> _emailRelayTokenRepositoryMock = null!;
    private Mock<ILogger<DataCleanupService>> _loggerMock = null!;
    private VernetztDbContext _dbContext = null!;

    [TestInitialize]
    public void Setup()
    {
        _auditLogRepositoryMock = new Mock<IAuditLogRepository>();
        _invitationTokenRepositoryMock = new Mock<IInvitationTokenRepository>();
        _emailRelayTokenRepositoryMock = new Mock<IEmailRelayTokenRepository>();
        _loggerMock = new Mock<ILogger<DataCleanupService>>();

        _dbContext = SqliteTestDatabase.CreateContext();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }

    private DataCleanupService CreateService(int auditRetentionDays = 90, int tokenRetentionDays = 30)
    {
        var appSettings = Options.Create(new AppSettings
        {
            AuditLogRetentionDays = auditRetentionDays,
            ExpiredTokenRetentionDays = tokenRetentionDays
        });

        return new DataCleanupService(
            _dbContext,
            _auditLogRepositoryMock.Object,
            _invitationTokenRepositoryMock.Object,
            _emailRelayTokenRepositoryMock.Object,
            appSettings,
            _loggerMock.Object);
    }

    [TestMethod]
    public async Task RunCleanupAsync_DeletesOldAuditLogs()
    {
        _auditLogRepositoryMock.Setup(r => r.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(15);
        _invitationTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _emailRelayTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = CreateService();
        var result = await service.RunCleanupAsync();

        Assert.AreEqual(15, result.DeletedAuditLogs);
        _auditLogRepositoryMock.Verify(r => r.DeleteOlderThanAsync(
            It.Is<DateTime>(d => d < DateTime.UtcNow.AddDays(-89)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RunCleanupAsync_DeletesExpiredInvitationTokens()
    {
        _auditLogRepositoryMock.Setup(r => r.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invitationTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _emailRelayTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = CreateService();
        var result = await service.RunCleanupAsync();

        Assert.AreEqual(5, result.DeletedInvitationTokens);
    }

    [TestMethod]
    public async Task RunCleanupAsync_DeletesExpiredEmailRelayTokens()
    {
        _auditLogRepositoryMock.Setup(r => r.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invitationTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _emailRelayTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var service = CreateService();
        var result = await service.RunCleanupAsync();

        Assert.AreEqual(3, result.DeletedEmailRelayTokens);
    }

    [TestMethod]
    public async Task RunCleanupAsync_UsesConfiguredRetentionDays()
    {
        _auditLogRepositoryMock.Setup(r => r.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invitationTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _emailRelayTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = CreateService(auditRetentionDays: 30, tokenRetentionDays: 7);
        await service.RunCleanupAsync();

        _auditLogRepositoryMock.Verify(r => r.DeleteOlderThanAsync(
            It.Is<DateTime>(d => d > DateTime.UtcNow.AddDays(-31) && d < DateTime.UtcNow.AddDays(-29)),
            It.IsAny<CancellationToken>()), Times.Once);

        _invitationTokenRepositoryMock.Verify(r => r.DeleteExpiredAsync(
            It.Is<DateTime>(d => d > DateTime.UtcNow.AddDays(-8) && d < DateTime.UtcNow.AddDays(-6)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RunCleanupAsync_ReturnsTotalDeleted()
    {
        _auditLogRepositoryMock.Setup(r => r.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        _invitationTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _emailRelayTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var service = CreateService();
        var result = await service.RunCleanupAsync();

        Assert.AreEqual(10, result.DeletedAuditLogs);
        Assert.AreEqual(5, result.DeletedInvitationTokens);
        Assert.AreEqual(3, result.DeletedEmailRelayTokens);
        Assert.IsTrue(result.TotalDeleted >= 18);
    }

    [TestMethod]
    public async Task RunCleanupAsync_NoDataToClean_ReturnsZeros()
    {
        _auditLogRepositoryMock.Setup(r => r.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invitationTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _emailRelayTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = CreateService();
        var result = await service.RunCleanupAsync();

        Assert.AreEqual(0, result.TotalDeleted);
        Assert.AreEqual(0, result.DeletedAuditLogs);
        Assert.AreEqual(0, result.DeletedInvitationTokens);
        Assert.AreEqual(0, result.DeletedEmailRelayTokens);
    }

    [TestMethod]
    public async Task RunCleanupAsync_SetsTimestamp()
    {
        _auditLogRepositoryMock.Setup(r => r.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invitationTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _emailRelayTokenRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var before = DateTime.UtcNow;
        var service = CreateService();
        var result = await service.RunCleanupAsync();
        var after = DateTime.UtcNow;

        Assert.IsTrue(result.CleanupTimestamp >= before && result.CleanupTimestamp <= after);
    }
}
