using Igevo.Vernetzt.Core.DTOs.Clubs;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Exceptions;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static Igevo.Vernetzt.Tests.Controllers.ControllerTestHelper;
using IAuthorizationService = Igevo.Vernetzt.Core.Interfaces.Services.IAuthorizationService;

namespace Igevo.Vernetzt.Tests.Controllers;

/// <summary>
/// Contact person assignment and location handling on the club and department endpoints.
/// </summary>
[TestClass]
public class ContactPersonControllerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private Mock<IClubService> _clubService = null!;
    private Mock<IDepartmentService> _departmentService = null!;
    private ClubsController _clubs = null!;
    private DepartmentsController _departments = null!;

    [TestInitialize]
    public void Setup()
    {
        _clubService = new Mock<IClubService>();
        _departmentService = new Mock<IDepartmentService>();
        var authorization = new Mock<IAuthorizationService>();
        _clubs = new ClubsController(
            _clubService.Object,
            _departmentService.Object,
            authorization.Object,
            new Mock<IClubTypeRepository>().Object,
            null!,
            NullLogger<ClubsController>.Instance).WithUser(_userId);
        _departments = new DepartmentsController(
            _departmentService.Object,
            _clubService.Object,
            authorization.Object,
            null!,
            NullLogger<DepartmentsController>.Instance).WithUser(_userId);
    }

    private void SetupClubAdd(Func<Task<bool>> result) =>
        _clubService.Setup(s => s.AddContactPersonAsync(_userId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(result);

    private void SetupDepartmentAdd(Func<Task<bool>> result) =>
        _departmentService.Setup(s => s.AddContactPersonAsync(_userId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(result);

    [TestMethod]
    public async Task ClubAddContactPerson_Success_PassesLocationFlag()
    {
        SetupClubAdd(() => Task.FromResult(true));

        var result = await _clubs.AddContactPerson(Guid.NewGuid(), Guid.NewGuid(), receivesLocationInfo: true);

        Assert.AreEqual(200, StatusOf(result));
        _clubService.Verify(s => s.AddContactPersonAsync(_userId, It.IsAny<Guid>(), It.IsAny<Guid>(), null, false, null, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ClubAddContactPerson_AlreadyAssigned_ReturnsConflict()
    {
        SetupClubAdd(() => throw ContactAssignmentConflictException.For(pending: true));

        var result = await _clubs.AddContactPerson(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(409, StatusOf(result));
    }

    [TestMethod]
    public async Task ClubUpdateContactPosition_PassesLocationFlag()
    {
        _clubService.Setup(s => s.UpdateContactPersonAsync(_userId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<string?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _clubs.UpdateContactPosition(Guid.NewGuid(), Guid.NewGuid(), receivesLocationInfo: true);

        Assert.AreEqual(200, StatusOf(result));
    }

    [TestMethod]
    public async Task ClubCreateAndUpdate_InvalidLocation_ReturnsBadRequest()
    {
        _clubService.Setup(s => s.CreateAsync(_userId, It.IsAny<Club>(), It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyCollection<Guid>?>()))
            .ThrowsAsync(new InvalidOperationException("Unbekannter Standort."));
        _clubService.Setup(s => s.UpdateAsync(_userId, It.IsAny<Guid>(), It.IsAny<Club>(), It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyCollection<Guid>?>()))
            .ThrowsAsync(new InvalidOperationException("Unbekannter Standort."));

        var create = await _clubs.Create(new CreateClubDto { Name = "Verein", LocationIds = [Guid.NewGuid()] }, CancellationToken.None);
        var update = await _clubs.Update(Guid.NewGuid(), new UpdateClubDto { Name = "Verein", LocationIds = [Guid.NewGuid()] }, CancellationToken.None);

        Assert.AreEqual(400, StatusOf(create));
        Assert.AreEqual(400, StatusOf(update));
    }

    [TestMethod]
    public async Task DepartmentAddContactPerson_Success_ReturnsNoContent()
    {
        SetupDepartmentAdd(() => Task.FromResult(true));

        var result = await _departments.AddContactPerson(Guid.NewGuid(), Guid.NewGuid(), receivesLocationInfo: true);

        Assert.IsInstanceOfType<NoContentResult>(result);
    }

    [TestMethod]
    public async Task DepartmentAddContactPerson_AlreadyAssigned_ReturnsConflict()
    {
        SetupDepartmentAdd(() => throw ContactAssignmentConflictException.For(pending: false));

        var result = await _departments.AddContactPerson(Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(409, StatusOf(result));
    }

    [TestMethod]
    public async Task DepartmentUpdateContactPosition_PassesLocationFlag()
    {
        _departmentService.Setup(s => s.UpdateContactPersonAsync(_userId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<string?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _departments.UpdateContactPosition(Guid.NewGuid(), Guid.NewGuid(), receivesLocationInfo: true);

        Assert.IsInstanceOfType<NoContentResult>(result);
    }
}
