using Igevo.Vernetzt.Core.DTOs.Events;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Server.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class EventMediaPosterTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _clubId = Guid.NewGuid();
    private Mock<IEventRepository> _eventRepository = null!;
    private Mock<IFileStorageService> _fileStorage = null!;
    private Mock<IImageProcessingService> _imageProcessing = null!;
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
        _eventRepository
            .Setup(r => r.AddMediaAsync(It.IsAny<EventMedia>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EventMedia m, CancellationToken _) => m);

        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(a => a.CanEditClubAsync(_userId, _clubId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _fileStorage = new Mock<IFileStorageService>();
        _fileStorage
            .Setup(f => f.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-media/stored.jpg");

        _imageProcessing = new Mock<IImageProcessingService>();
        SetProcessingResult(withPreview: true);

        _service = new EventService(
            _eventRepository.Object,
            authorization.Object,
            Mock.Of<IAuditLogRepository>(),
            Mock.Of<IClubRepository>(),
            Mock.Of<IDepartmentRepository>(),
            _fileStorage.Object,
            _imageProcessing.Object,
            NullLogger<EventService>.Instance);
    }

    private void SetProcessingResult(bool withPreview)
    {
        _imageProcessing
            .Setup(p => p.ProcessEventMediaAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventMediaProcessingResult
            {
                Success = true,
                Data = [1, 2, 3],
                ContentType = "image/jpeg",
                Extension = ".jpg",
                Width = 1000,
                Height = 1414,
                Variants = withPreview
                    ? [new ProcessedImage { Data = [1], Width = 480, Height = 679 }]
                    : []
            });
    }

    private EventMedia AddMedia(EventMediaType type, bool preview = true)
    {
        var media = new EventMedia
        {
            Id = Guid.NewGuid(),
            EventId = _event.Id,
            Type = type,
            VariantWidths = preview ? "160,480" : null
        };
        _event.Media.Add(media);
        return media;
    }

    private Task<EventMedia> Upload(EventMediaType type) =>
        _service.UploadMediaAsync(_userId, _event.Id, new MemoryStream([1, 2, 3]), "plakat.jpg", type);

    [TestMethod]
    public async Task NewCover_DemotesPreviousCoverToImage()
    {
        var previous = AddMedia(EventMediaType.Cover);
        var other = AddMedia(EventMediaType.Image);

        await _service.UpdateMediaAsync(_userId, _event.Id, other.Id, EventMediaType.Cover, null, null, null);

        Assert.AreEqual(EventMediaType.Image, previous.Type);
        Assert.AreEqual(EventMediaType.Cover, other.Type);
    }

    [TestMethod]
    public async Task UploadPoster_DemotesPreviousPosterToImage()
    {
        var previous = AddMedia(EventMediaType.Poster);

        await Upload(EventMediaType.Poster);

        Assert.AreEqual(EventMediaType.Image, previous.Type);
        _eventRepository.Verify(r => r.AddMediaAsync(
            It.Is<EventMedia>(m => m.Type == EventMediaType.Poster), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UploadPoster_WithoutPreview_IsRejected()
    {
        SetProcessingResult(withPreview: false);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Upload(EventMediaType.Poster));

        _eventRepository.Verify(r => r.AddMediaAsync(It.IsAny<EventMedia>(), It.IsAny<CancellationToken>()), Times.Never);
        _fileStorage.Verify(f => f.UploadFileAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UploadPdfAsPoster_IsRejectedWithProcessingMessage()
    {
        _imageProcessing
            .Setup(p => p.ProcessEventMediaAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventMediaProcessingResult { Success = false, ErrorMessage = "Dateityp wird nicht unterstützt." });

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Upload(EventMediaType.Poster));

        StringAssert.Contains(ex.Message, "nicht unterstützt");
        _eventRepository.Verify(r => r.AddMediaAsync(It.IsAny<EventMedia>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateToPoster_MovesPosterToOtherMedia()
    {
        var first = AddMedia(EventMediaType.Poster);
        var second = AddMedia(EventMediaType.Image);

        await _service.UpdateMediaAsync(_userId, _event.Id, second.Id, EventMediaType.Poster, null, null, null);

        Assert.AreEqual(EventMediaType.Image, first.Type);
        Assert.AreEqual(EventMediaType.Poster, second.Type);
        Assert.AreEqual(1, _event.Media.Count(m => m.Type == EventMediaType.Poster));
    }

    [TestMethod]
    public async Task UpdateToPoster_WithoutPreview_IsRejected()
    {
        var pdf = AddMedia(EventMediaType.Attachment, preview: false);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _service.UpdateMediaAsync(_userId, _event.Id, pdf.Id, EventMediaType.Poster, null, null, null));

        Assert.AreEqual(EventMediaType.Attachment, pdf.Type);
    }

    [TestMethod]
    public async Task UpdatePosterToImage_RemovesThePoster()
    {
        var poster = AddMedia(EventMediaType.Poster);

        await _service.UpdateMediaAsync(_userId, _event.Id, poster.Id, EventMediaType.Image, null, null, null);

        Assert.AreEqual(EventMediaType.Image, poster.Type);
        Assert.AreEqual(0, _event.Media.Count(m => m.Type == EventMediaType.Poster));
    }

    [TestMethod]
    public async Task UndefinedType_IsRejected()
    {
        var media = AddMedia(EventMediaType.Image);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Upload((EventMediaType)99));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _service.UpdateMediaAsync(_userId, _event.Id, media.Id, (EventMediaType)99, null, null, null));

        Assert.AreEqual(EventMediaType.Image, media.Type);
    }

    private static EventMediaDto Dto(EventMediaType type, bool preview = true) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Variants = preview ? [new EventMediaVariantDto { Width = 480, Url = "/v/480" }] : []
    };

    [TestMethod]
    public void ResolveDisplayImage_PrefersCoverThenImage()
    {
        var cover = Dto(EventMediaType.Cover);
        var image = Dto(EventMediaType.Image);
        var poster = Dto(EventMediaType.Poster);

        Assert.AreSame(cover, EventsController.ResolveDisplayImage([poster, image, cover]));
        Assert.AreSame(image, EventsController.ResolveDisplayImage([poster, image]));
    }

    [TestMethod]
    public void ResolveDisplayImage_UsesPosterOnlyAsLastResort()
    {
        var poster = Dto(EventMediaType.Poster);

        Assert.AreSame(poster, EventsController.ResolveDisplayImage([poster]));
        Assert.IsNull(EventsController.ResolveDisplayImage([Dto(EventMediaType.Attachment, preview: false)]));
    }

    [TestMethod]
    public void ResolvePoster_ReturnsPosterWithPreviewOnly()
    {
        var poster = Dto(EventMediaType.Poster);

        Assert.AreSame(poster, EventsController.ResolvePoster([Dto(EventMediaType.Cover), poster]));
        Assert.IsNull(EventsController.ResolvePoster([Dto(EventMediaType.Poster, preview: false)]));
        Assert.IsNull(EventsController.ResolvePoster([Dto(EventMediaType.Cover)]));
    }
}
