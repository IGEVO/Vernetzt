using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class EventMediaUsageTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _clubId = Guid.NewGuid();
    private Mock<IEventRepository> _eventRepository = null!;
    private Event _event = null!;
    private EventService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _event = new Event { Id = Guid.NewGuid(), ClubId = _clubId, Title = "Sommerfest" };
        _eventRepository = new Mock<IEventRepository>();
        _eventRepository.Setup(r => r.GetByIdAsync(_event.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_event);
        _eventRepository
            .Setup(r => r.GetMediaByEventAsync(_event.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _event.Media.ToList());

        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(a => a.CanEditClubAsync(_userId, _clubId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _service = new EventService(
            _eventRepository.Object,
            authorization.Object,
            Mock.Of<IAuditLogRepository>(),
            Mock.Of<IClubRepository>(),
            Mock.Of<IDepartmentRepository>(),
            Mock.Of<IFileStorageService>(),
            Mock.Of<IImageProcessingService>(),
            NullLogger<EventService>.Instance);
    }

    private EventMedia AddMedia(EventMediaUsage usages = EventMediaUsage.None, bool preview = true)
    {
        var media = new EventMedia
        {
            Id = Guid.NewGuid(),
            EventId = _event.Id,
            Type = EventMediaType.Image,
            Usages = usages,
            VariantWidths = preview ? "160,480" : null
        };
        _event.Media.Add(media);
        return media;
    }

    [TestMethod]
    public async Task AssigningView_RemovesItFromOtherMedia()
    {
        var banner = AddMedia(EventMediaUsage.Header | EventMediaUsage.List);
        var square = AddMedia();

        await _service.UpdateMediaAsync(_userId, _event.Id, square.Id, null, null, null, EventMediaUsage.List);

        Assert.AreEqual(EventMediaUsage.List, square.Usages);
        Assert.AreEqual(EventMediaUsage.Header, banner.Usages);
        _eventRepository.Verify(r => r.UpdateMediaAsync(
            It.Is<IEnumerable<EventMedia>>(m => m.Contains(banner) && m.Contains(square)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UnknownUsageBits_AreIgnored()
    {
        var media = AddMedia();

        await _service.UpdateMediaAsync(_userId, _event.Id, media.Id, null, null, null, (EventMediaUsage)(64 | 2));

        Assert.AreEqual(EventMediaUsage.Header, media.Usages);
    }

    [TestMethod]
    public async Task MediaWithoutPreview_CannotBeAssignedToView()
    {
        var pdf = AddMedia(preview: false);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _service.UpdateMediaAsync(_userId, _event.Id, pdf.Id, null, null, null, EventMediaUsage.Card));
    }
}
