using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Locations;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Igevo.Vernetzt.Tests.Controllers;

[TestClass]
public class LocationsControllerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private Mock<ILocationService> _service = null!;
    private LocationsController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new Mock<ILocationService>();
        _controller = new LocationsController(_service.Object).WithUser(_userId);
    }

    [TestMethod]
    public async Task GetActive_ReturnsSummariesOnly()
    {
        _service.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LocationDto { Id = Guid.NewGuid(), Name = "Sporthalle Nord", City = "Bochum", UsageCount = 2 }]);

        var result = await _controller.GetActive();

        var ok = (OkObjectResult)result;
        var body = (ApiResponse<IReadOnlyList<LocationSummaryDto>>)ok.Value!;
        Assert.AreEqual("Sporthalle Nord", body.Data!.Single().Name);
    }

    [TestMethod]
    public async Task GetAll_ReturnsDetails()
    {
        _service.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LocationDto { Name = "Hallenbad", UsageCount = 3 }]);

        var result = await _controller.GetAll();

        var body = (ApiResponse<IReadOnlyList<LocationDto>>)((OkObjectResult)result).Value!;
        Assert.AreEqual(3, body.Data!.Single().UsageCount);
    }

    [TestMethod]
    public async Task Create_PassesCurrentUser()
    {
        var dto = new SaveLocationDto { Name = "Hallenbad" };
        _service.Setup(s => s.CreateAsync(_userId, dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationDto { Name = "Hallenbad" });

        var result = await _controller.Create(dto);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        _service.Verify(s => s.CreateAsync(_userId, dto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Create_ValidationError_ReturnsBadRequest()
    {
        _service.Setup(s => s.CreateAsync(_userId, It.IsAny<SaveLocationDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ein Standort mit diesem Namen existiert bereits."));

        var result = await _controller.Create(new SaveLocationDto { Name = "Doppelt" });

        var badRequest = (BadRequestObjectResult)result;
        var body = (ApiResponse<object>)badRequest.Value!;
        Assert.AreEqual("VALIDATION_ERROR", body.Error!.Code);
        Assert.AreEqual("Ein Standort mit diesem Namen existiert bereits.", body.Error.Message);
    }

    [TestMethod]
    public async Task Update_UnknownLocation_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _service.Setup(s => s.UpdateAsync(_userId, id, It.IsAny<SaveLocationDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.Update(id, new SaveLocationDto { Name = "X" });

        Assert.IsInstanceOfType<NotFoundObjectResult>(result);
    }

    [TestMethod]
    public async Task Update_ReturnsUpdatedLocation()
    {
        var id = Guid.NewGuid();
        _service.Setup(s => s.UpdateAsync(_userId, id, It.IsAny<SaveLocationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationDto { Id = id, Name = "Neu" });

        var result = await _controller.Update(id, new SaveLocationDto { Name = "Neu" });

        var body = (ApiResponse<LocationDto>)((OkObjectResult)result).Value!;
        Assert.AreEqual(id, body.Data!.Id);
    }

    [TestMethod]
    public async Task Delete_ReturnsTrue()
    {
        var id = Guid.NewGuid();

        var result = await _controller.Delete(id);

        var body = (ApiResponse<bool>)((OkObjectResult)result).Value!;
        Assert.IsTrue(body.Data);
        _service.Verify(s => s.DeleteAsync(_userId, id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
