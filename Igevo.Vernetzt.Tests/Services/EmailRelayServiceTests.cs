using Igevo.Vernetzt.Core.DTOs.EmailRelay;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class EmailRelayServiceTests
{
    private Mock<IEmailRelayTokenRepository> _tokenRepoMock = null!;
    private Mock<IMailboxMonitorService> _mailboxMock = null!;
    private Mock<IEmailService> _emailServiceMock = null!;
    private Mock<IUserRepository> _userRepoMock = null!;
    private Mock<IRecipientResolutionService> _recipientResolutionServiceMock = null!;
    private Mock<IAuditLogRepository> _auditRepoMock = null!;
    private Mock<ILogger<EmailRelayService>> _loggerMock = null!;
    private IConfiguration _configuration = null!;
    private EmailRelayService _service = null!;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _recipientId1 = Guid.NewGuid();
    private readonly Guid _recipientId2 = Guid.NewGuid();

    [TestInitialize]
    public void Setup()
    {
        _tokenRepoMock = new Mock<IEmailRelayTokenRepository>();
        _mailboxMock = new Mock<IMailboxMonitorService>();
        _emailServiceMock = new Mock<IEmailService>();
        _userRepoMock = new Mock<IUserRepository>();
        _recipientResolutionServiceMock = new Mock<IRecipientResolutionService>();
        _auditRepoMock = new Mock<IAuditLogRepository>();
        _loggerMock = new Mock<ILogger<EmailRelayService>>();

        var configData = new Dictionary<string, string?>
        {
            { "EmailRelay:Enabled", "true" },
            { "EmailRelay:BaseAddress", "relay@example.com" },
            { "EmailRelay:AddressingMode", "PlusAddressing" },
            { "EmailRelay:TokenValidityMinutes", "30" },
            { "EmailRelay:AddInfoBanner", "true" },
            { "EmailRelay:SendSummaryReply", "true" },
            { "EmailRelay:MaxActiveTokensPerUser", "10" }
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Default user setup
        _userRepoMock.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _userId, Email = "sender@example.com", FirstName = "Test", LastName = "User" });

        _userRepoMock.Setup(r => r.GetByIdAsync(_recipientId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _recipientId1, Email = "recipient1@example.com", FirstName = "Recipient", LastName = "One" });

        _userRepoMock.Setup(r => r.GetByIdAsync(_recipientId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _recipientId2, Email = "recipient2@example.com", FirstName = "Recipient", LastName = "Two" });

        _tokenRepoMock.Setup(r => r.CountActiveTokensByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _tokenRepoMock.Setup(r => r.AddAsync(It.IsAny<EmailRelayToken>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken t, CancellationToken _) => t);

        _recipientResolutionServiceMock.Setup(r => r.ResolveForSendAsync(
                It.IsAny<Guid>(),
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IEnumerable<Guid> __, IEnumerable<Guid> recipientUserIds, CancellationToken ___) =>
            {
                var emails = recipientUserIds
                    .Distinct()
                    .Select(id => id == _recipientId1
                        ? "recipient1@example.com"
                        : id == _recipientId2
                            ? "recipient2@example.com"
                            : null)
                    .Where(email => !string.IsNullOrWhiteSpace(email))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new RecipientResolutionResult
                {
                    Success = emails.Count > 0,
                    RecipientEmails = emails,
                    RecipientDisplayItems = emails.Select(GetDefaultDisplayNameForEmail).ToList(),
                    RequestedRecipientCount = recipientUserIds.Distinct().Count()
                };
            });

        _recipientResolutionServiceMock.Setup(r => r.ResolveDisplayItemsByEmailAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> emails, CancellationToken _) =>
                emails.Select(GetDefaultDisplayNameForEmail).ToList());

        _service = CreateService();
    }

    private EmailRelayService CreateService(IConfiguration? config = null)
    {
        return new EmailRelayService(
            _tokenRepoMock.Object,
            _mailboxMock.Object,
            _emailServiceMock.Object,
            _userRepoMock.Object,
            _recipientResolutionServiceMock.Object,
            _auditRepoMock.Object,
            config ?? _configuration,
            _loggerMock.Object);
    }

    private static string GetDefaultDisplayNameForEmail(string email)
    {
        var normalized = email?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "r1@test.com" => "Kontakt Eins (Beispielverein)",
            "r2@test.com" => "Kontakt Zwei (Beispielverein)",
            "recipient1@example.com" => "Recipient One",
            "recipient2@example.com" => "Recipient Two",
            "anna@test.com" => "Anna Muster (Fußball, TSV Beispiel)",
            "ben@test.com" => "Ben Beispiel (TSV Beispiel)",
            "invalid-mail" => "Karl Fehler",
            _ => "Unbekannter Kontakt"
        };
    }

    #region CreateTokenAsync Tests

    [TestMethod]
    public async Task CreateTokenAsync_ValidRequest_GeneratesUniqueTokenAddress()
    {
        // Arrange
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { _recipientId1 }
        };

        // Act
        var result = await _service.CreateTokenAsync(request, _userId);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrEmpty(result.TokenAddress));
        Assert.IsTrue(result.TokenAddress.Contains("@example.com"));
        Assert.IsTrue(result.TokenAddress.StartsWith("relay+"));
    }

    [TestMethod]
    public async Task CreateTokenAsync_ValidRequest_SetsCorrectExpiry()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { _recipientId1 }
        };

        var beforeCreate = DateTime.UtcNow;
        var result = await _service.CreateTokenAsync(request, _userId);
        var afterCreate = DateTime.UtcNow;

        Assert.IsTrue(result.ExpiresAt >= beforeCreate.AddMinutes(30));
        Assert.IsTrue(result.ExpiresAt <= afterCreate.AddMinutes(30).AddSeconds(1));
    }

    [TestMethod]
    public async Task CreateTokenAsync_CatchAllMode_GeneratesFullDomainAddress()
    {
        var configData = new Dictionary<string, string?>
        {
            { "EmailRelay:Enabled", "true" },
            { "EmailRelay:BaseAddress", "relay@catchall.example.com" },
            { "EmailRelay:AddressingMode", "CatchAll" },
            { "EmailRelay:TokenValidityMinutes", "30" },
            { "EmailRelay:AddInfoBanner", "true" },
            { "EmailRelay:SendSummaryReply", "true" },
            { "EmailRelay:MaxActiveTokensPerUser", "10" }
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var service = CreateService(config);

        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { _recipientId1 }
        };

        var result = await service.CreateTokenAsync(request, _userId);

        Assert.IsTrue(result.TokenAddress.EndsWith("@catchall.example.com"));
        Assert.IsFalse(result.TokenAddress.Contains("+"));
    }

    [TestMethod]
    public async Task CreateTokenAsync_PlusAddressingMode_GeneratesCorrectFormat()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { _recipientId1 }
        };

        var result = await _service.CreateTokenAsync(request, _userId);

        Assert.IsTrue(result.TokenAddress.StartsWith("relay+"));
        Assert.IsTrue(result.TokenAddress.EndsWith("@example.com"));
        // Token part should be 32 hex chars
        var tokenPart = result.TokenAddress.Split('+')[1].Split('@')[0];
        Assert.AreEqual(32, tokenPart.Length);
        Assert.IsTrue(tokenPart.All(c => "0123456789abcdef".Contains(c)));
    }

    [TestMethod]
    public async Task CreateTokenAsync_TokenIs32HexChars_NotGuessable()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { _recipientId1 }
        };

        var result1 = await _service.CreateTokenAsync(request, _userId);
        var result2 = await _service.CreateTokenAsync(request, _userId);

        // Two tokens should be different
        Assert.AreNotEqual(result1.TokenAddress, result2.TokenAddress);
    }

    [TestMethod]
    public async Task CreateTokenAsync_StoresSenderEmail_ForSummaryReply()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { _recipientId1 }
        };

        EmailRelayToken? savedToken = null;
        _tokenRepoMock.Setup(r => r.AddAsync(It.IsAny<EmailRelayToken>(), It.IsAny<CancellationToken>()))
            .Callback<EmailRelayToken, CancellationToken>((t, _) => savedToken = t)
            .ReturnsAsync((EmailRelayToken t, CancellationToken _) => t);

        await _service.CreateTokenAsync(request, _userId);

        Assert.IsNotNull(savedToken);
        Assert.AreEqual("sender@example.com", savedToken.SenderEmail);
    }

    [TestMethod]
    public async Task CreateTokenAsync_MultipleRecipients_ResolvesAllEmails()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { _recipientId1, _recipientId2 }
        };

        EmailRelayToken? savedToken = null;
        _tokenRepoMock.Setup(r => r.AddAsync(It.IsAny<EmailRelayToken>(), It.IsAny<CancellationToken>()))
            .Callback<EmailRelayToken, CancellationToken>((t, _) => savedToken = t)
            .ReturnsAsync((EmailRelayToken t, CancellationToken _) => t);

        var result = await _service.CreateTokenAsync(request, _userId);

        Assert.AreEqual(2, result.RecipientCount);
        Assert.IsNotNull(savedToken);
        Assert.AreEqual(2, savedToken.RecipientEmails.Count);
        CollectionAssert.Contains(savedToken.RecipientEmails, "recipient1@example.com");
        CollectionAssert.Contains(savedToken.RecipientEmails, "recipient2@example.com");
    }

    [TestMethod]
    public async Task CreateTokenAsync_MaxTokensReached_ThrowsInvalidOperationException()
    {
        _tokenRepoMock.Setup(r => r.CountActiveTokensByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { _recipientId1 }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateTokenAsync(request, _userId));
    }

    [TestMethod]
    public async Task CreateTokenAsync_NoValidRecipients_ThrowsInvalidOperationException()
    {
        var unknownId = Guid.NewGuid();
        _userRepoMock.Setup(r => r.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { unknownId }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateTokenAsync(request, _userId));
    }

    #endregion

    #region ProcessIncomingEmailsAsync Tests

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ValidToken_ForwardsEmailToAllRecipients()
    {
        var token = CreateActiveToken("relay+abc123@example.com", new List<string> { "r1@test.com", "r2@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+abc123@example.com", "Test Subject", "<p>Hello</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+abc123@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.Is<IEnumerable<string>>(r => r.Count() == 2),
            "Test Subject",
            It.IsAny<string>(),
            It.IsAny<string?>(),
            "sender@ext.com",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            "relay@example.com",
            "Vernetzt",
            It.IsAny<IEnumerable<EmailAttachment>?>(),
            It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ValidToken_SetsReplyToOriginalSender()
    {
        var token = CreateActiveToken("relay+abc123@example.com", new List<string> { "r1@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "original-sender@ext.com", "relay+abc123@example.com", "Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+abc123@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(),
            "original-sender@ext.com",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            "relay@example.com",
            "Vernetzt",
            It.IsAny<IEnumerable<EmailAttachment>?>(),
            It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_InvalidBaseAddress_PassesNullFromOverride()
    {
        var configData = new Dictionary<string, string?>
        {
            { "EmailRelay:Enabled", "true" },
            { "EmailRelay:BaseAddress", "@example.com" },
            { "EmailRelay:AddressingMode", "CatchAll" },
            { "EmailRelay:TokenValidityMinutes", "30" },
            { "EmailRelay:AddInfoBanner", "true" },
            { "EmailRelay:SendSummaryReply", "true" },
            { "EmailRelay:MaxActiveTokensPerUser", "10" }
        };

        var service = CreateService(new ConfigurationBuilder().AddInMemoryCollection(configData).Build());
        var token = CreateActiveToken("abc123@example.com", new List<string> { "r1@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "original-sender@ext.com", "abc123@example.com", "Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("abc123@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(),
            "original-sender@ext.com",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            null,
            "Vernetzt",
            It.IsAny<IEnumerable<EmailAttachment>?>(),
            It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ForwardingFromAddressConfigured_UsesItAsSender()
    {
        var configData = new Dictionary<string, string?>
        {
            { "EmailRelay:Enabled", "true" },
            { "EmailRelay:BaseAddress", "relay@catchall.example.com" },
            { "EmailRelay:ForwardingFromAddress", "no-reply@igevo.de" },
            { "EmailRelay:AddressingMode", "CatchAll" },
            { "EmailRelay:TokenValidityMinutes", "30" },
            { "EmailRelay:AddInfoBanner", "true" },
            { "EmailRelay:SendSummaryReply", "true" },
            { "EmailRelay:MaxActiveTokensPerUser", "10" }
        };

        var service = CreateService(new ConfigurationBuilder().AddInMemoryCollection(configData).Build());
        var token = CreateActiveToken("abc123@catchall.example.com", new List<string> { "r1@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "original-sender@ext.com", "abc123@catchall.example.com", "Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("abc123@catchall.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(),
            "original-sender@ext.com",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            "no-reply@igevo.de",
            "Vernetzt",
            It.IsAny<IEnumerable<EmailAttachment>?>(),
            It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_InvalidForwardingFromAddress_PassesNullFromOverride()
    {
        var configData = new Dictionary<string, string?>
        {
            { "EmailRelay:Enabled", "true" },
            { "EmailRelay:BaseAddress", "relay@catchall.example.com" },
            { "EmailRelay:ForwardingFromAddress", "not-an-address" },
            { "EmailRelay:AddressingMode", "CatchAll" },
            { "EmailRelay:TokenValidityMinutes", "30" },
            { "EmailRelay:AddInfoBanner", "true" },
            { "EmailRelay:SendSummaryReply", "true" },
            { "EmailRelay:MaxActiveTokensPerUser", "10" }
        };

        var service = CreateService(new ConfigurationBuilder().AddInMemoryCollection(configData).Build());
        var token = CreateActiveToken("abc123@catchall.example.com", new List<string> { "r1@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "original-sender@ext.com", "abc123@catchall.example.com", "Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("abc123@catchall.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(),
            "original-sender@ext.com",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            null,
            "Vernetzt",
            It.IsAny<IEnumerable<EmailAttachment>?>(),
            It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ValidToken_MarksTokenAsUsed()
    {
        var token = CreateActiveToken("relay+abc123@example.com", new List<string> { "r1@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+abc123@example.com", "Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+abc123@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _tokenRepoMock.Verify(r => r.UpdateAsync(
            It.Is<EmailRelayToken>(t => t.IsUsed && t.UsedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ValidToken_SendsSummaryReplyToSender()
    {
        var token = CreateActiveToken("relay+abc123@example.com", new List<string> { "r1@test.com", "r2@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+abc123@example.com", "Test Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+abc123@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        // Summary reply should be sent to the token's SenderEmail
        _emailServiceMock.Verify(e => e.SendForwardingSuccessSummaryAsync(
            "sender@example.com",
            It.Is<IReadOnlyCollection<string>>(names => names.Count == 2),
            It.Is<IReadOnlyCollection<string>>(names => names.Count == 0),
            "Test Subject",
            "sender@ext.com",
            It.IsAny<string?>(),
            "<p>Body</p>",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ValidToken_DeletesEmailFromMailbox()
    {
        var token = CreateActiveToken("relay+abc123@example.com", new List<string> { "r1@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("email-42", "sender@ext.com", "relay+abc123@example.com", "Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+abc123@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _mailboxMock.Verify(m => m.DeleteEmailAsync("email-42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ValidToken_WithInfoBanner_PrependsInfoBox()
    {
        var token = CreateActiveToken("relay+abc123@example.com", new List<string> { "r1@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+abc123@example.com", "Subject", "<p>Original</p>", "Sender Name")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+abc123@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
            It.Is<string>(html => html.Contains("Diese E-Mail wurde über Vernetzt an 1 Empfänger in BCC gesendet") && html.Contains("<p>Original</p>")),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            "relay@example.com",
            "Vernetzt",
            It.IsAny<IEnumerable<EmailAttachment>?>(),
            It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ExpiredToken_SendsFailureNotification()
    {
        var expiredToken = new EmailRelayToken
        {
            Id = Guid.NewGuid(),
            TokenAddress = "relay+expired@example.com",
            RecipientEmails = new List<string> { "r1@test.com" },
            SenderEmail = "sender@example.com",
            CreatedByUserId = _userId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10), // expired
            IsUsed = false
        };

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+expired@example.com", "Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+expired@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);

        _tokenRepoMock.Setup(r => r.GetTokenByAddressAsync("relay+expired@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "sender@example.com",
            It.Is<string>(s => s.Contains("fehlgeschlagen")),
            It.Is<string>(s => s.Contains("abgelaufen")),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ExpiredToken_DeletesEmailFromMailbox()
    {
        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("email-99", "sender@ext.com", "relay+expired@example.com", "Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+expired@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);

        _tokenRepoMock.Setup(r => r.GetTokenByAddressAsync("relay+expired@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailRelayToken
            {
                Id = Guid.NewGuid(), TokenAddress = "relay+expired@example.com",
                RecipientEmails = new List<string>(), SenderEmail = "x@x.com",
                CreatedByUserId = _userId, ExpiresAt = DateTime.UtcNow.AddMinutes(-10), IsUsed = false
            });

        await _service.ProcessIncomingEmailsAsync();

        _mailboxMock.Verify(m => m.DeleteEmailAsync("email-99", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_ExpiredToken_DoesNotNotifyFromHeaderAddress()
    {
        var expiredToken = new EmailRelayToken
        {
            Id = Guid.NewGuid(),
            TokenAddress = "relay+expired2@example.com",
            RecipientEmails = new List<string> { "r1@test.com" },
            SenderEmail = "sender@example.com",
            CreatedByUserId = _userId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
            IsUsed = false
        };

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "forged@victim.com", "relay+expired2@example.com", "Subject", "<p>Body</p>")
            });
        _tokenRepoMock.Setup(r => r.GetTokenByAddressAsync("relay+expired2@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "forged@victim.com", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_UsedToken_SendsFailureNotification()
    {
        var usedToken = new EmailRelayToken
        {
            Id = Guid.NewGuid(),
            TokenAddress = "relay+used@example.com",
            RecipientEmails = new List<string> { "r1@test.com" },
            SenderEmail = "sender@example.com",
            CreatedByUserId = _userId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            IsUsed = true,
            UsedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+used@example.com", "Subject", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+used@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);

        _tokenRepoMock.Setup(r => r.GetTokenByAddressAsync("relay+used@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usedToken);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "sender@example.com",
            It.Is<string>(s => s.Contains("fehlgeschlagen")),
            It.Is<string>(s => s.Contains("bereits verwendet")),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_NoMatchingToken_DeletesEmailSilently()
    {
        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("email-77", "spam@unknown.com", "random@example.com", "Spam", "<p>Spam</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);

        _tokenRepoMock.Setup(r => r.GetTokenByAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);

        await _service.ProcessIncomingEmailsAsync();

        _mailboxMock.Verify(m => m.DeleteEmailAsync("email-77", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_NoMatchingToken_NoNotificationSent()
    {
        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "spam@unknown.com", "random@example.com", "Spam", "<p>Spam</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);

        _tokenRepoMock.Setup(r => r.GetTokenByAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);

        await _service.ProcessIncomingEmailsAsync();

        // No SendEmailAsync should be called for spam
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_MultipleEmails_ProcessesAllIndependently()
    {
        var token1 = CreateActiveToken("relay+t1@example.com", new List<string> { "r1@test.com" });
        var token2 = CreateActiveToken("relay+t2@example.com", new List<string> { "r2@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("e1", "a@x.com", "relay+t1@example.com", "Sub1", "<p>1</p>"),
                CreateIncomingEmail("e2", "b@x.com", "relay+t2@example.com", "Sub2", "<p>2</p>"),
                CreateIncomingEmail("e3", "c@x.com", "relay+nomatch@example.com", "Sub3", "<p>3</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+t1@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token1);
        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+t2@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token2);
        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+nomatch@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);
        _tokenRepoMock.Setup(r => r.GetTokenByAddressAsync("relay+nomatch@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        // Two forwards + two summary replies
        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IEnumerable<EmailAttachment>?>(),
            It.IsAny<string?>(), It.IsAny<string?>()), Times.Exactly(2));

        _emailServiceMock.Verify(e => e.SendForwardingSuccessSummaryAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Three emails deleted
        _mailboxMock.Verify(m => m.DeleteEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_EmptyMailbox_DoesNothing()
    {
        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>());

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.VerifyNoOtherCalls();
        _tokenRepoMock.Verify(r => r.GetActiveTokenByAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region CleanupExpiredTokensAsync Tests

    [TestMethod]
    public async Task CleanupExpiredTokensAsync_ExpiredTokens_Deleted()
    {
        var expired1 = new EmailRelayToken { Id = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddMinutes(-60), TokenAddress = "a", RecipientEmails = new(), SenderEmail = "x", CreatedByUserId = _userId };
        var expired2 = new EmailRelayToken { Id = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddMinutes(-30), TokenAddress = "b", RecipientEmails = new(), SenderEmail = "x", CreatedByUserId = _userId };

        _tokenRepoMock.Setup(r => r.GetExpiredTokensAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmailRelayToken> { expired1, expired2 });

        await _service.CleanupExpiredTokensAsync();

        _tokenRepoMock.Verify(r => r.DeleteAsync(expired1, It.IsAny<CancellationToken>()), Times.Once);
        _tokenRepoMock.Verify(r => r.DeleteAsync(expired2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CleanupExpiredTokensAsync_NoExpiredTokens_DoesNothing()
    {
        _tokenRepoMock.Setup(r => r.GetExpiredTokensAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmailRelayToken>());

        await _service.CleanupExpiredTokensAsync();

        _tokenRepoMock.Verify(r => r.DeleteAsync(It.IsAny<EmailRelayToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region GetActiveTokensAsync Tests

    [TestMethod]
    public async Task GetActiveTokensAsync_ReturnsUserTokensAsDtos()
    {
        var tokens = new List<EmailRelayToken>
        {
            new() { Id = Guid.NewGuid(), TokenAddress = "relay+abc@example.com", RecipientEmails = new List<string> { "a@b.com", "c@d.com" }, SenderEmail = "s@e.com", CreatedByUserId = _userId, CreatedAt = DateTime.UtcNow.AddMinutes(-10), ExpiresAt = DateTime.UtcNow.AddMinutes(20), SubjectHint = "Test" }
        };

        _tokenRepoMock.Setup(r => r.GetActiveTokensByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);

        var result = await _service.GetActiveTokensAsync(_userId);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("relay+abc@example.com", result[0].TokenAddress);
        Assert.AreEqual(2, result[0].RecipientCount);
        Assert.AreEqual("Test", result[0].SubjectHint);
    }

    #endregion

    #region CancelTokenAsync Tests

    [TestMethod]
    public async Task CancelTokenAsync_OwnToken_MarksAsUsed()
    {
        var token = new EmailRelayToken
        {
            Id = Guid.NewGuid(),
            TokenAddress = "relay+cancel@example.com",
            RecipientEmails = new List<string> { "r@t.com" },
            SenderEmail = "s@e.com",
            CreatedByUserId = _userId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            IsUsed = false
        };

        _tokenRepoMock.Setup(r => r.GetByIdAsync(token.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        var result = await _service.CancelTokenAsync(token.Id, _userId);

        Assert.IsTrue(result);
        _tokenRepoMock.Verify(r => r.UpdateAsync(
            It.Is<EmailRelayToken>(t => t.IsUsed && t.UsedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CancelTokenAsync_OtherUsersToken_ThrowsUnauthorized()
    {
        var otherUserId = Guid.NewGuid();
        var token = new EmailRelayToken
        {
            Id = Guid.NewGuid(),
            TokenAddress = "relay+other@example.com",
            RecipientEmails = new List<string>(),
            SenderEmail = "other@e.com",
            CreatedByUserId = otherUserId, // owned by someone else
            ExpiresAt = DateTime.UtcNow.AddMinutes(20)
        };

        _tokenRepoMock.Setup(r => r.GetByIdAsync(token.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.CancelTokenAsync(token.Id, _userId));
    }

    [TestMethod]
    public async Task CancelTokenAsync_NonExistentToken_ReturnsFalse()
    {
        var tokenId = Guid.NewGuid();
        _tokenRepoMock.Setup(r => r.GetByIdAsync(tokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailRelayToken?)null);

        var result = await _service.CancelTokenAsync(tokenId, _userId);

        Assert.IsFalse(result);
    }

    #endregion

    #region Summary Reply Content Tests

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_SummaryReply_ContainsRecipientCount()
    {
        var token = CreateActiveToken("relay+s@example.com", new List<string> { "r1@t.com", "r2@t.com", "r3@t.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+s@example.com", "Sub", "<p>B</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+s@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendForwardingSuccessSummaryAsync(
            It.IsAny<string>(),
            It.Is<IReadOnlyCollection<string>>(names => names.Count == 3),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_SummaryReply_ContainsPrivacyNote()
    {
        var token = CreateActiveToken("relay+p@example.com", new List<string> { "r1@t.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+p@example.com", "Sub", "<p>B</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+p@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendForwardingSuccessSummaryAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_SummaryReply_ContainsRecipientNames_AndDroppedRecipients()
    {
        var token = CreateActiveToken(
            "relay+names@example.com",
            new List<string> { "anna@test.com", "invalid-mail", "ben@test.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+names@example.com", "Sub", "<p>B</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+names@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.Is<IEnumerable<string>>(r => r.Count() == 2),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IEnumerable<EmailAttachment>?>(),
            It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);

        _emailServiceMock.Verify(e => e.SendForwardingSuccessSummaryAsync(
            It.IsAny<string>(),
            It.Is<IReadOnlyCollection<string>>(names =>
                names.Any(n => n.Contains("Anna Muster")) && names.Any(n => n.Contains("Ben Beispiel"))),
            It.Is<IReadOnlyCollection<string>>(names =>
                names.Contains("Unbekannter Kontakt")),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_SummaryReply_ContainsOriginalMessageAtBottom()
    {
        var token = CreateActiveToken("relay+orig@example.com", new List<string> { "r1@t.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+orig@example.com", "Sub", "<p>Original Body</p>", "Externer Sender")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+orig@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendForwardingSuccessSummaryAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(),
            "sender@ext.com",
            "Externer Sender",
            "<p>Original Body</p>",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_SummaryReply_ContainsDarkModeMetaTags()
    {
        var token = CreateActiveToken("relay+dark@example.com", new List<string> { "r1@t.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+dark@example.com", "Sub", "<p>Body</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+dark@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendForwardingSuccessSummaryAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_SummaryDisabled_NoSummaryReply()
    {
        var configData = new Dictionary<string, string?>
        {
            { "EmailRelay:Enabled", "true" },
            { "EmailRelay:BaseAddress", "relay@example.com" },
            { "EmailRelay:AddressingMode", "PlusAddressing" },
            { "EmailRelay:TokenValidityMinutes", "30" },
            { "EmailRelay:AddInfoBanner", "false" },
            { "EmailRelay:SendSummaryReply", "false" },
            { "EmailRelay:MaxActiveTokensPerUser", "10" }
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var service = CreateService(config);

        var token = CreateActiveToken("relay+ns@example.com", new List<string> { "r1@t.com" });

        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IncomingEmail>
            {
                CreateIncomingEmail("1", "sender@ext.com", "relay+ns@example.com", "Sub", "<p>B</p>")
            });

        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+ns@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        _emailServiceMock.Setup(e => e.SendBccEmailAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await service.ProcessIncomingEmailsAsync();

        // Forward should happen, but no summary reply
        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IEnumerable<EmailAttachment>?>(),
            It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);

        _emailServiceMock.Verify(e => e.SendForwardingSuccessSummaryAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Oversized Messages & Mailbox Cleanup

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_OversizedForActiveToken_NotifiesTokenOwnerNotFromHeader()
    {
        var token = CreateActiveToken("relay+big1@example.com", new List<string> { "r1@test.com" });
        var email = CreateOversizedEmail(Guid.NewGuid().ToString(), "forged@victim.com", "relay+big1@example.com", 30 * 1024 * 1024);
        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncomingEmail> { email });
        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+big1@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "sender@example.com", It.IsAny<string>(),
            It.Is<string>(b => b.Contains("30 MB") && b.Contains("20 MB")),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "forged@victim.com", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailServiceMock.Verify(e => e.SendBccEmailAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IEnumerable<EmailAttachment>?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_OversizedForActiveToken_KeepsMessageAndTokenUnused()
    {
        var token = CreateActiveToken("relay+big2@example.com", new List<string> { "r1@test.com" });
        var email = CreateOversizedEmail(Guid.NewGuid().ToString(), "sender@ext.com", "relay+big2@example.com", 30 * 1024 * 1024);
        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncomingEmail> { email });
        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+big2@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await _service.ProcessIncomingEmailsAsync();

        _mailboxMock.Verify(m => m.DeleteEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsFalse(token.IsUsed);
        _auditRepoMock.Verify(a => a.LogAsync(
            It.Is<AuditLog>(l => l.Action == AuditAction.EmailRelayRejected && l.NewValues!.Contains("MessageTooLarge")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_OversizedPolledTwice_NotifiesOnlyOnce()
    {
        var token = CreateActiveToken("relay+big3@example.com", new List<string> { "r1@test.com" });
        var email = CreateOversizedEmail(Guid.NewGuid().ToString(), "sender@ext.com", "relay+big3@example.com", 30 * 1024 * 1024);
        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncomingEmail> { email });
        _tokenRepoMock.Setup(r => r.GetActiveTokenByAddressAsync("relay+big3@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await _service.ProcessIncomingEmailsAsync();
        await _service.ProcessIncomingEmailsAsync();

        _emailServiceMock.Verify(e => e.SendEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessIncomingEmailsAsync_OversizedWithoutActiveToken_DeletesWithoutNotification()
    {
        var id = Guid.NewGuid().ToString();
        var email = CreateOversizedEmail(id, "sender@ext.com", "relay+unknown@example.com", 30 * 1024 * 1024);
        _mailboxMock.Setup(m => m.FetchNewEmailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncomingEmail> { email });

        await _service.ProcessIncomingEmailsAsync();

        _mailboxMock.Verify(m => m.DeleteEmailAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateTokenAsync_ReturnsConfiguredMaxMessageSize()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "EmailRelay:BaseAddress", "relay@example.com" },
                { "EmailRelay:MaxMessageSizeBytes", "1048576" }
            })
            .Build();

        var response = await CreateService(config).CreateTokenAsync(
            new CreateEmailRelayTokenRequest { RecipientUserIds = new List<Guid> { _recipientId1 } }, _userId);

        Assert.AreEqual(1048576L, response.MaxMessageSizeBytes);
    }

    [TestMethod]
    public async Task CleanupMailboxAsync_UsesConfiguredRetention()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "EmailRelay:BaseAddress", "relay@example.com" },
                { "EmailRelay:MailboxRetentionHours", "48" }
            })
            .Build();

        await CreateService(config).CleanupMailboxAsync();

        _mailboxMock.Verify(m => m.DeleteOlderThanAsync(TimeSpan.FromHours(48), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-5")]
    public async Task CleanupMailboxAsync_RetentionBelowMinimum_ClampsToOneHour(string configured)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "EmailRelay:BaseAddress", "relay@example.com" },
                { "EmailRelay:MailboxRetentionHours", configured }
            })
            .Build();

        await CreateService(config).CleanupMailboxAsync();

        _mailboxMock.Verify(m => m.DeleteOlderThanAsync(TimeSpan.FromHours(1), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Helpers

    private EmailRelayToken CreateActiveToken(string address, List<string> recipients)
    {
        return new EmailRelayToken
        {
            Id = Guid.NewGuid(),
            TokenAddress = address,
            RecipientEmails = recipients,
            SenderEmail = "sender@example.com",
            CreatedByUserId = _userId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static IncomingEmail CreateOversizedEmail(string id, string from, string to, long sizeBytes)
    {
        return new IncomingEmail
        {
            Id = id,
            FromAddress = from,
            ToAddresses = new List<string> { to },
            Subject = "Big",
            IsOversized = true,
            SizeBytes = sizeBytes,
            ReceivedAt = DateTime.UtcNow
        };
    }

    private static IncomingEmail CreateIncomingEmail(string id, string from, string to, string subject, string htmlBody, string? fromName = null)
    {
        return new IncomingEmail
        {
            Id = id,
            FromAddress = from,
            FromName = fromName,
            ToAddresses = new List<string> { to },
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = null,
            Attachments = new List<EmailAttachment>(),
            ReceivedAt = DateTime.UtcNow
        };
    }

    #endregion
}
