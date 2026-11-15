using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.DTOs.Admin;
using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using static Igevo.Vernetzt.Tests.Controllers.ControllerTestHelper;

namespace Igevo.Vernetzt.Tests.Controllers;

[TestClass]
public class ConsentAndEmailChangeControllerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private Mock<IContactAssignmentConsentService> _consent = null!;
    private Mock<IAccountEmailChangeService> _emailChange = null!;
    private UsersController _users = null!;

    [TestInitialize]
    public void Setup()
    {
        _consent = new Mock<IContactAssignmentConsentService>();
        _emailChange = new Mock<IAccountEmailChangeService>();
        _users = new UsersController(
            new Mock<IUserService>().Object,
            new Mock<IAuthService>().Object,
            _consent.Object,
            _emailChange.Object,
            NullLogger<UsersController>.Instance).WithUser(_userId);
    }

    [TestMethod]
    [DataRow(ContactAssignmentConsentStatus.Success, 200)]
    [DataRow(ContactAssignmentConsentStatus.NotFound, 404)]
    [DataRow(ContactAssignmentConsentStatus.Forbidden, 403)]
    [DataRow(ContactAssignmentConsentStatus.Conflict, 409)]
    [DataRow(ContactAssignmentConsentStatus.TooManyRequests, 429)]
    public async Task ConsentActions_MapStatusToHttp(ContactAssignmentConsentStatus status, int expected)
    {
        var assignmentId = Guid.NewGuid();
        var result = new ContactAssignmentConsentResult(status, "Nachricht");
        _consent.Setup(s => s.ConfirmAsync(_userId, assignmentId, VisibilityLevel.Internal, null, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        _consent.Setup(s => s.DeclineAsync(_userId, assignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        _consent.Setup(s => s.SendReminderAsync(_userId, assignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(result);

        var confirm = await _users.ConfirmMyContactAssignment(assignmentId, new ConfirmContactAssignmentDto { Visibility = VisibilityLevel.Internal });
        var decline = await _users.DeclineMyContactAssignment(assignmentId);
        var reminder = await _users.SendContactAssignmentConsentReminder(assignmentId);

        Assert.AreEqual(expected, StatusOf(confirm));
        Assert.AreEqual(expected, StatusOf(decline));
        Assert.AreEqual(expected, StatusOf(reminder));
    }

    [TestMethod]
    [DataRow(AccountEmailChangeStatus.ConfirmationSent, 200)]
    [DataRow(AccountEmailChangeStatus.NotFound, 404)]
    [DataRow(AccountEmailChangeStatus.Forbidden, 403)]
    [DataRow(AccountEmailChangeStatus.Conflict, 409)]
    [DataRow(AccountEmailChangeStatus.SendFailed, 502)]
    [DataRow(AccountEmailChangeStatus.Invalid, 400)]
    public async Task RequestUserEmailChange_MapsStatusToHttp(AccountEmailChangeStatus status, int expected)
    {
        var targetId = Guid.NewGuid();
        _emailChange.Setup(s => s.RequestChangeAsync(_userId, targetId, "neu@example.org", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountEmailChangeRequestResult(status, "Nachricht"));

        var result = await _users.RequestUserEmailChange(targetId, new RequestAccountEmailChangeDto { NewEmail = "neu@example.org" });

        Assert.AreEqual(expected, StatusOf(result));
    }

    [TestMethod]
    public async Task ConfirmEmailChange_UnexpectedError_RendersErrorPage()
    {
        _emailChange.Setup(s => s.ConfirmChangeAsync("token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var controller = new AccountEmailChangeController(
            _emailChange.Object,
            Options.Create(new AppSettings { BaseUrl = "https://vernetzt.example/" }),
            NullLogger<AccountEmailChangeController>.Instance);

        var result = (ContentResult)await controller.Confirm("token", CancellationToken.None);

        Assert.AreEqual(500, result.StatusCode);
        Assert.Contains("<h1>Fehler</h1>", result.Content!);
        Assert.Contains("https://vernetzt.example/login", result.Content!);
        Assert.DoesNotContain("boom", result.Content!);
    }

    [TestMethod]
    public async Task PendingItems_UnauthorizedAccess_ReturnsForbidden()
    {
        var service = new Mock<IPendingItemsService>();
        service.Setup(s => s.GetOverviewAsync(_userId, It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        service.Setup(s => s.RevokeInvitationAsync(_userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        var controller = new PendingItemsController(service.Object, NullLogger<PendingItemsController>.Instance).WithUser(_userId);

        var overview = await controller.GetOverview();
        var revoke = await controller.RevokeInvitation(Guid.NewGuid());

        Assert.AreEqual(403, ((ObjectResult)overview.Result!).StatusCode);
        Assert.AreEqual(403, StatusOf(revoke));
    }

    [TestMethod]
    public async Task PendingItems_RevokeInvitation_ReturnsOk()
    {
        var invitationId = Guid.NewGuid();
        var service = new Mock<IPendingItemsService>();
        service.Setup(s => s.RevokeInvitationAsync(_userId, invitationId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        service.Setup(s => s.GetOverviewAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(new PendingItemsOverviewDto());
        var controller = new PendingItemsController(service.Object, NullLogger<PendingItemsController>.Instance).WithUser(_userId);

        Assert.AreEqual(200, StatusOf(await controller.RevokeInvitation(invitationId)));
        Assert.IsInstanceOfType<OkObjectResult>((await controller.GetOverview()).Result);
    }
}
