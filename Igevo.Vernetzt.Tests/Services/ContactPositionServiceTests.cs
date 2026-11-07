using Igevo.Vernetzt.Core.DTOs.ContactPositions;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class ContactPositionServiceTests
{
    private Mock<IContactPositionRepository> _repositoryMock = null!;
    private Mock<ILogger<ContactPositionService>> _loggerMock = null!;
    private ContactPositionService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _repositoryMock = new Mock<IContactPositionRepository>();
        _loggerMock = new Mock<ILogger<ContactPositionService>>();

        _service = new ContactPositionService(
            _repositoryMock.Object,
            _loggerMock.Object);
    }

    #region GetByIdAsync Tests

    [TestMethod]
    public async Task GetByIdAsync_ExistingPosition_ReturnsDto()
    {
        // Arrange
        var positionId = Guid.NewGuid();
        var position = new ContactPosition
        {
            Id = positionId,
            Name = "Test Position",
            Description = "Test Description",
            CreatedAt = DateTime.UtcNow
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(positionId, default))
            .ReturnsAsync(position);

        // Act
        var result = await _service.GetByIdAsync(positionId);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(positionId, result.Id);
        Assert.AreEqual("Test Position", result.Name);
        Assert.AreEqual("Test Description", result.Description);
    }

    [TestMethod]
    public async Task GetByIdAsync_NonExistingPosition_ReturnsNull()
    {
        // Arrange
        var positionId = Guid.NewGuid();

        _repositoryMock.Setup(r => r.GetByIdAsync(positionId, default))
            .ReturnsAsync((ContactPosition?)null);

        // Act
        var result = await _service.GetByIdAsync(positionId);

        // Assert
        Assert.IsNull(result);
    }

    #endregion

    #region GetAllAsync Tests

    [TestMethod]
    public async Task GetAllAsync_ReturnsAllPositions()
    {
        // Arrange
        var positions = new List<ContactPosition>
        {
            new ContactPosition
            {
                Id = Guid.NewGuid(),
                Name = "Position 1",
                CreatedAt = DateTime.UtcNow
            },
            new ContactPosition
            {
                Id = Guid.NewGuid(),
                Name = "Position 2",
                CreatedAt = DateTime.UtcNow
            }
        };

        _repositoryMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(positions);

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Position 1", result[0].Name);
        Assert.AreEqual("Position 2", result[1].Name);
    }

    [TestMethod]
    public async Task GetAllAsync_NoPositions_ReturnsEmptyList()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(new List<ContactPosition>());

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    #endregion

    #region CreateAsync Tests

    [TestMethod]
    public async Task CreateAsync_ValidDto_CreatesAndReturnsPosition()
    {
        // Arrange
        var createDto = new CreateContactPositionDto
        {
            Name = "New Position",
            Description = "New Description",
        };

        var createdPosition = new ContactPosition
        {
            Id = Guid.NewGuid(),
            Name = createDto.Name,
            Description = createDto.Description,
            CreatedAt = DateTime.UtcNow
        };

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ContactPosition>(), default))
            .ReturnsAsync(createdPosition);

        // Act
        var result = await _service.CreateAsync(createDto);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("New Position", result.Name);
        Assert.AreEqual("New Description", result.Description);
        _repositoryMock.Verify(r => r.CreateAsync(It.Is<ContactPosition>(p =>
            p.Name == "New Position" &&
            p.Description == "New Description"), default), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_TrimsWhitespace()
    {
        // Arrange
        var createDto = new CreateContactPositionDto
        {
            Name = "  Whitespace Name  ",
            Description = "  Whitespace Description  ",
        };

        var createdPosition = new ContactPosition
        {
            Id = Guid.NewGuid(),
            Name = "Whitespace Name",
            Description = "Whitespace Description",
            CreatedAt = DateTime.UtcNow
        };

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ContactPosition>(), default))
            .ReturnsAsync(createdPosition);

        // Act
        var result = await _service.CreateAsync(createDto);

        // Assert
        _repositoryMock.Verify(r => r.CreateAsync(It.Is<ContactPosition>(p =>
            p.Name == "Whitespace Name" &&
            p.Description == "Whitespace Description"), default), Times.Once);
    }

    #endregion

    #region UpdateAsync Tests

    [TestMethod]
    public async Task UpdateAsync_ExistingPosition_UpdatesAndReturnsPosition()
    {
        // Arrange
        var positionId = Guid.NewGuid();
        var existingPosition = new ContactPosition
        {
            Id = positionId,
            Name = "Old Name",
            Description = "Old Description",
            CreatedAt = DateTime.UtcNow
        };

        var updateDto = new UpdateContactPositionDto
        {
            Name = "Updated Name",
            Description = "Updated Description",
        };

        var updatedPosition = new ContactPosition
        {
            Id = positionId,
            Name = updateDto.Name,
            Description = updateDto.Description,
            CreatedAt = existingPosition.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(positionId, default))
            .ReturnsAsync(existingPosition);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<ContactPosition>(), default))
            .ReturnsAsync(updatedPosition);

        // Act
        var result = await _service.UpdateAsync(positionId, updateDto);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("Updated Name", result.Name);
        Assert.AreEqual("Updated Description", result.Description);
        _repositoryMock.Verify(r => r.UpdateAsync(It.Is<ContactPosition>(p =>
            p.Id == positionId &&
            p.Name == "Updated Name"), default), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAsync_NonExistingPosition_ThrowsException()
    {
        // Arrange
        var positionId = Guid.NewGuid();
        var updateDto = new UpdateContactPositionDto
        {
            Name = "Updated Name",
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(positionId, default))
            .ReturnsAsync((ContactPosition?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.UpdateAsync(positionId, updateDto));
    }

    #endregion

    #region DeleteAsync Tests

    [TestMethod]
    public async Task DeleteAsync_ExistingPosition_ReturnsTrue()
    {
        // Arrange
        var positionId = Guid.NewGuid();

        _repositoryMock.Setup(r => r.DeleteAsync(positionId, default))
            .ReturnsAsync(true);

        // Act
        var result = await _service.DeleteAsync(positionId);

        // Assert
        Assert.IsTrue(result);
        _repositoryMock.Verify(r => r.DeleteAsync(positionId, default), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAsync_NonExistingPosition_ReturnsFalse()
    {
        // Arrange
        var positionId = Guid.NewGuid();

        _repositoryMock.Setup(r => r.DeleteAsync(positionId, default))
            .ReturnsAsync(false);

        // Act
        var result = await _service.DeleteAsync(positionId);

        // Assert
        Assert.IsFalse(result);
    }

    #endregion
}
