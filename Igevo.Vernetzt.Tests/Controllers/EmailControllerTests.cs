using Ganss.Xss;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Email;
using Igevo.Vernetzt.Core.DTOs.EmailRelay;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Controllers;

[TestClass]
public class EmailControllerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _clubId = Guid.NewGuid();
    private Mock<IEmailService> _emailService = null!;
    private Mock<IRecipientResolutionService> _resolution = null!;
    private EmailController _controller = null!;
    private List<string>? _sentTo;

    [TestInitialize]
    public void Setup()
    {
        _emailService = new Mock<IEmailService>();
        _emailService
            .Setup(s => s.SendBccEmailAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback((IEnumerable<string> recipients, string _, string _, string? _, string? _, string? _, CancellationToken _,
                string? _, string? _, IEnumerable<EmailAttachment>? _, string? _, string? _) => _sentTo = recipients.ToList())
            .ReturnsAsync(true);

        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetByIdAsync(_userId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _userId, Email = "mo@example.test", FirstName = "Mo", LastName = "Manager" });

        _resolution = new Mock<IRecipientResolutionService>();
        _resolution.Setup(r => r.ResolveForSendAsync(_userId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecipientResolutionResult
            {
                Success = true,
                RecipientEmails = ["kai@example.test", "info@tsv.test"],
                RecipientDisplayItems = ["Kai Kasse (TSV)", "Vorstand TSV"]
            });

        _controller = new EmailController(
            _emailService.Object,
            userService.Object,
            _resolution.Object,
            new Mock<IAuditLogRepository>().Object,
            new HtmlSanitizer(),
            new Mock<ILogger<EmailController>>().Object).WithUser(_userId);
    }

    private SendBulkEmailRequest Request() => new()
    {
        Subject = "Rundschreiben",
        HtmlBody = "<p>Hallo</p>",
        RecipientAssignmentIds = [Guid.NewGuid()],
        RecipientClubIds = [_clubId]
    };

    [TestMethod]
    public async Task SendBulkEmail_ClubAddresses_AreAddedWithoutDuplicates()
    {
        _resolution.Setup(r => r.ResolveClubAddressesAsync(_userId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecipientResolutionResult
            {
                Success = true,
                RecipientEmails = ["INFO@tsv.test", "info@harmonie.test"],
                RecipientDisplayItems = ["TSV (Vereinsadresse)", "Harmonie (Vereinsadresse)"]
            });

        var result = await _controller.SendBulkEmail(Request());

        var body = (ApiResponse<SendBulkEmailResponse>)((OkObjectResult)result.Result!).Value!;
        Assert.AreEqual(3, body.Data!.RecipientCount);
        CollectionAssert.AreEqual(new[] { "kai@example.test", "info@tsv.test", "info@harmonie.test" }, _sentTo);
    }

    [TestMethod]
    public async Task SendBulkEmail_ForbiddenClubAddresses_Returns403WithoutSending()
    {
        _resolution.Setup(r => r.ResolveClubAddressesAsync(_userId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecipientResolutionResult { Forbidden = true });

        var result = await _controller.SendBulkEmail(Request());

        Assert.AreEqual(403, ControllerTestHelper.StatusOf(result.Result!));
        Assert.IsNull(_sentTo);
    }
}
