using System.Net.Mail;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class EmailServiceTests
{
    private Mock<ILogger<EmailService>> _loggerMock = null!;
    private Mock<ISmtpSender> _smtpSenderMock = null!;
    private Mock<Core.Interfaces.Services.IAppSettingsService> _appSettingsServiceMock = null!;
    private EmailService _service = null!;

    // Captures the last MailMessage passed to ISmtpSender.SendAsync
    // NOTE: MailMessage is disposed after SendAsync returns, so we extract content here.
    private MailMessage? _sentMessage;
    private string? _sentHtmlBody;
    private string? _sentPlainBody;

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Email:From:Address", "system@vernetzt.local" },
                { "Email:From:Name", "Vernetzt" },
                { "App:BaseUrl", "https://vernetzt.local" }
            })
            .Build();

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<EmailService>>();
        _appSettingsServiceMock = new Mock<Core.Interfaces.Services.IAppSettingsService>();
        _smtpSenderMock = new Mock<ISmtpSender>();

        _smtpSenderMock
            .Setup(s => s.SendAsync(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MailMessage, CancellationToken>((msg, _) =>
            {
                _sentMessage = msg;
                var htmlView = msg.AlternateViews.FirstOrDefault(v => v.ContentType.MediaType == "text/html");
                if (htmlView != null)
                {
                    htmlView.ContentStream.Position = 0;
                    using var r = new System.IO.StreamReader(htmlView.ContentStream, leaveOpen: true);
                    _sentHtmlBody = r.ReadToEnd();
                }
                var plainView = msg.AlternateViews.FirstOrDefault(v => v.ContentType.MediaType == "text/plain");
                if (plainView != null)
                {
                    plainView.ContentStream.Position = 0;
                    using var r = new System.IO.StreamReader(plainView.ContentStream, leaveOpen: true);
                    _sentPlainBody = r.ReadToEnd();
                }
            })
            .Returns(Task.CompletedTask);

        _service = new EmailService(
            BuildConfig(),
            _loggerMock.Object,
            _appSettingsServiceMock.Object,
            _smtpSenderMock.Object);
    }

    // --- Guard / validation tests ---

    [TestMethod]
    public async Task SendEmailAsync_WithEmptyRecipient_ReturnsFalse_AndDoesNotSend()
    {
        var result = await _service.SendEmailAsync("", "Subject", "<p>Body</p>");

        Assert.IsFalse(result);
        _smtpSenderMock.Verify(s => s.SendAsync(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SendBulkEmailAsync_WithEmptyRecipients_ReturnsZero_AndDoesNotSend()
    {
        var result = await _service.SendBulkEmailAsync(Enumerable.Empty<string>(), "Subject", "<p>Body</p>");

        Assert.AreEqual(0, result);
        _smtpSenderMock.Verify(s => s.SendAsync(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SendBccEmailAsync_WithEmptyRecipients_ReturnsFalse_AndDoesNotSend()
    {
        var result = await _service.SendBccEmailAsync(Enumerable.Empty<string>(), "Subject", "<p>Body</p>");

        Assert.IsFalse(result);
        _smtpSenderMock.Verify(s => s.SendAsync(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Content / template tests ---

    [TestMethod]
    public async Task SendEmailAsync_SendsToCorrectRecipient()
    {
        await _service.SendEmailAsync("recipient@example.com", "Hello", "<p>Hi</p>");

        Assert.IsNotNull(_sentMessage);
        Assert.AreEqual("recipient@example.com", _sentMessage!.To[0].Address);
    }

    [TestMethod]
    public async Task SendEmailAsync_UsesConfiguredFromAddress()
    {
        await _service.SendEmailAsync("to@example.com", "Subject", "<p>Body</p>");

        Assert.AreEqual("system@vernetzt.local", _sentMessage!.From!.Address);
        Assert.AreEqual("Vernetzt", _sentMessage.From.DisplayName);
    }

    [TestMethod]
    public async Task SendEmailAsync_SetsSubjectAndBody()
    {
        await _service.SendEmailAsync("to@example.com", "Test Subject", "<p>Test Body</p>");

        Assert.AreEqual("Test Subject", _sentMessage!.Subject);
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, "<p>Test Body</p>");
    }

    [TestMethod]
    public async Task SendEmailAsync_ReturnsTrue_WhenSenderSucceeds()
    {
        var result = await _service.SendEmailAsync("to@example.com", "Subject", "<p>Body</p>");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task SendEmailAsync_ReturnsFalse_WhenSenderThrows()
    {
        _smtpSenderMock
            .Setup(s => s.SendAsync(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        var result = await _service.SendEmailAsync("to@example.com", "Subject", "<p>Body</p>");

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task SendContextEmailLoginHintAsync_UsesBrandedTemplate_WithLoginLink()
    {
        var sent = await _service.SendContextEmailLoginHintAsync("vorstand@verein.test");

        Assert.IsTrue(sent);
        Assert.AreEqual("vorstand@verein.test", _sentMessage!.To[0].Address);
        Assert.AreEqual("Hinweis zur Anmeldung bei Vernetzt", _sentMessage.Subject);
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, "class='container'");
        StringAssert.Contains(_sentHtmlBody, "https://vernetzt.local/login");
        StringAssert.Contains(_sentHtmlBody, "Amtsadresse");
        Assert.IsNotNull(_sentPlainBody, "Expected a plain-text alternate view");
        StringAssert.Contains(_sentPlainBody, "https://vernetzt.local/login");
    }

    [TestMethod]
    public async Task SendUserCreatedNotificationAsync_SendsToCorrectRecipient_WithUserNameInBody()
    {
        await _service.SendUserCreatedNotificationAsync("new@example.com", "Max Mustermann");

        Assert.AreEqual("new@example.com", _sentMessage!.To[0].Address);
        StringAssert.Contains(_sentMessage.Subject, "Willkommen");
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, "Max Mustermann");
    }

    [TestMethod]
    public async Task SendClubAssignmentNotificationAsync_ContainsClubNameInBody()
    {
        var clubId = Guid.NewGuid();
        await _service.SendClubAssignmentNotificationAsync(
            "user@example.com", "Max", "FC Example", clubId);

        StringAssert.Contains(_sentMessage!.Subject, "FC Example");
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, "FC Example");
        StringAssert.Contains(_sentHtmlBody, clubId.ToString());
    }

    [TestMethod]
    public async Task SendDepartmentAssignmentNotificationAsync_ContainsDepartmentAndClubNameInBody()
    {
        var deptId = Guid.NewGuid();
        await _service.SendDepartmentAssignmentNotificationAsync(
            "user@example.com", "Max", "Junioren", "FC Example", deptId);

        StringAssert.Contains(_sentMessage!.Subject, "Junioren");
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, "Junioren");
        StringAssert.Contains(_sentHtmlBody, "FC Example");
        StringAssert.Contains(_sentHtmlBody, deptId.ToString());
    }

    [TestMethod]
    public async Task SendServerStartupNotificationAsync_ContainsServerInfoInBody()
    {
        await _service.SendServerStartupNotificationAsync("admin@example.com", "Version 1.2.3");

        Assert.AreEqual("admin@example.com", _sentMessage!.To[0].Address);
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, "Version 1.2.3");
    }

    [TestMethod]
    public async Task SendBccEmailAsync_AddressesRecipientsAsBcc_NotTo()
    {
        var recipients = new[] { "a@example.com", "b@example.com", "c@example.com" };
        await _service.SendBccEmailAsync(recipients, "Newsletter", "<p>Hello</p>");

        Assert.AreEqual(0, _sentMessage!.To.Count);
        Assert.AreEqual(3, _sentMessage.Bcc.Count);
        CollectionAssert.AreEquivalent(
            recipients,
            _sentMessage.Bcc.Select(a => a.Address).ToArray());
    }

    [TestMethod]
    public async Task SendBccEmailAsync_SetsReplyToHeader_WhenProvided()
    {
        await _service.SendBccEmailAsync(
            new[] { "user@example.com" },
            "Subject",
            "<p>Body</p>",
            replyToEmail: "support@vernetzt.local",
            replyToName: "Support");

        Assert.AreEqual(1, _sentMessage!.ReplyToList.Count);
        Assert.AreEqual("support@vernetzt.local", _sentMessage.ReplyToList[0].Address);
        Assert.AreEqual("Support", _sentMessage.ReplyToList[0].DisplayName);
    }

    [TestMethod]
    public async Task SendMagicLinkEmailAsync_ContainsMagicLinkInBody()
    {
        var link = "https://vernetzt.local/auth/magic?token=abc123";
        await _service.SendMagicLinkEmailAsync("user@example.com", link, expirationMinutes: 15);

        Assert.AreEqual("user@example.com", _sentMessage!.To[0].Address);
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, link);
        StringAssert.Contains(_sentHtmlBody, "15");
    }

    [TestMethod]
    public async Task SendBulkEmailAsync_SendsOneMessagePerRecipient()
    {
        var recipients = new[] { "a@example.com", "b@example.com", "c@example.com" };
        var sentCount = 0;
        _smtpSenderMock
            .Setup(s => s.SendAsync(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MailMessage, CancellationToken>((_, _) => sentCount++)
            .Returns(Task.CompletedTask);

        var result = await _service.SendBulkEmailAsync(recipients, "Subject", "<p>Body</p>");

        Assert.AreEqual(3, result);
        Assert.AreEqual(3, sentCount);
    }

    [TestMethod]
    public async Task SendBccEmailAsync_InvalidFromOverride_FallsBackToConfiguredFromAddress()
    {
        var result = await _service.SendBccEmailAsync(
            new[] { "user@example.com" },
            "Subject",
            "<p>Body</p>",
            fromEmail: "@example.com",
            fromName: "Relay");

        Assert.IsTrue(result);
        Assert.IsNotNull(_sentMessage);
        Assert.AreEqual("system@vernetzt.local", _sentMessage!.From!.Address);
        Assert.AreEqual("Vernetzt", _sentMessage.From.DisplayName);
    }

    [TestMethod]
    public async Task SendBccEmailAsync_InvalidBccRecipient_IsSkipped()
    {
        var result = await _service.SendBccEmailAsync(
            new[] { "valid@example.com", "invalid-address" },
            "Subject",
            "<p>Body</p>");

        Assert.IsTrue(result);
        Assert.IsNotNull(_sentMessage);
        Assert.AreEqual(1, _sentMessage!.Bcc.Count);
        Assert.AreEqual("valid@example.com", _sentMessage.Bcc[0].Address);
    }

    [TestMethod]
    public async Task SendForwardingSuccessSummaryAsync_UsesVernetztPrefix_AndNoBulletList()
    {
        var result = await _service.SendForwardingSuccessSummaryAsync(
            senderEmail: "sender@example.com",
            forwardedRecipientDisplayItems: new[] { "Maria Mueller", "Max Mustermann" },
            droppedRecipientDisplayItems: Array.Empty<string>(),
            originalSubject: "Test",
            originalFromAddress: "sender@example.com",
            originalFromName: "Sender Name",
            originalHtmlBody: "<p>Hallo Welt</p>",
            originalTextBody: null);

        Assert.IsTrue(result);
        Assert.IsNotNull(_sentMessage);
        Assert.AreEqual("Vernetzt: Test — Weiterleitung erfolgreich", _sentMessage!.Subject);
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        Assert.IsFalse(_sentHtmlBody!.Contains("<ul", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(_sentHtmlBody, "Maria Mueller");
        StringAssert.Contains(_sentHtmlBody, "Max Mustermann");
    }

    [TestMethod]
    public async Task SendForwardingSuccessSummaryAsync_ConvertsOriginalHtmlToReadableText()
    {
        await _service.SendForwardingSuccessSummaryAsync(
            senderEmail: "sender@example.com",
            forwardedRecipientDisplayItems: new[] { "Empfaenger A" },
            droppedRecipientDisplayItems: Array.Empty<string>(),
            originalSubject: "Darkmode-Test",
            originalFromAddress: "sender@example.com",
            originalFromName: "Sender Name",
            originalHtmlBody: "<p style='color:#ffffff'>Hallo Welt</p>",
            originalTextBody: null);

        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, "Urspr");
        StringAssert.Contains(_sentHtmlBody, "Hallo Welt");
        Assert.IsFalse(_sentHtmlBody.Contains("color:#ffffff", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SendInvitationEmailAsync_MakesUrlsAbsoluteAndEncodesInviterName()
    {
        await _service.SendInvitationEmailAsync(
            "invitee@example.com", "Max <Admin>", "/onboarding?token=abc", "/api/invitations/revoke?token=abc", 7);

        Assert.AreEqual("invitee@example.com", _sentMessage!.To[0].Address);
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, "https://vernetzt.local/onboarding?token=abc");
        StringAssert.Contains(_sentHtmlBody, "https://vernetzt.local/api/invitations/revoke?token=abc");
        StringAssert.Contains(_sentHtmlBody, "Max &lt;Admin&gt;");
        StringAssert.Contains(_sentHtmlBody, "7 Tage");
        Assert.IsNotNull(_sentPlainBody, "Expected a plain-text alternate view");
        StringAssert.Contains(_sentPlainBody, "https://vernetzt.local/onboarding?token=abc");
    }

    [TestMethod]
    public async Task SendContactAssignmentEmailVerificationAsync_ContainsVerifyLink()
    {
        const string verifyUrl = "https://vernetzt.local/verify-contact-email?token=xyz";
        await _service.SendContactAssignmentEmailVerificationAsync("amt@verein.example", verifyUrl);

        Assert.AreEqual("amt@verein.example", _sentMessage!.To[0].Address);
        StringAssert.Contains(_sentMessage.Subject, "Amtsadresse");
        Assert.IsNotNull(_sentHtmlBody, "Expected an HTML alternate view");
        StringAssert.Contains(_sentHtmlBody, verifyUrl);
        Assert.IsNotNull(_sentPlainBody, "Expected a plain-text alternate view");
        StringAssert.Contains(_sentPlainBody, verifyUrl);
    }
}
