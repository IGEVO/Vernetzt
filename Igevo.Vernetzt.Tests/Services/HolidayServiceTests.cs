using Igevo.Vernetzt.Core.DTOs.Holidays;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Igevo.Vernetzt.Infrastructure.Repositories;
using Igevo.Vernetzt.Infrastructure.Services;
using Igevo.Vernetzt.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static Igevo.Vernetzt.Tests.Services.HolidayIcsParserTests;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class HolidayServiceTests
{
    private VernetztDbContext _db = null!;
    private FakeFetcher _fetcher = null!;
    private HolidayService _service = null!;
    private readonly Guid _userId = Guid.NewGuid();

    [TestInitialize]
    public void Setup()
    {
        _db = SqliteTestDatabase.CreateContext();
        _fetcher = new FakeFetcher();
        _service = new HolidayService(
            _db,
            _fetcher,
            new AuditLogRepository(_db, NullLogger<AuditLogRepository>.Instance),
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero)),
            NullLogger<HolidayService>.Instance);
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    [TestMethod]
    public async Task CreateSource_SynchronizesImmediately()
    {
        _fetcher.Content = Calendar(AllDay("h", "Herbstferien", "20261012", "20261025"));

        var source = await _service.CreateSourceAsync(_userId, new SaveHolidaySourceDto { Name = "Ferien NRW", Url = "webcal://example.org/nrw.ics" });

        Assert.AreEqual("https://example.org/nrw.ics", source.Url);
        Assert.AreEqual(1, source.EntryCount);
        Assert.IsNull(source.LastSyncError);
        Assert.IsNotNull(source.LastSyncedAt);

        var entries = await _service.GetEntriesAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.AreEqual("Herbstferien", entries.Single().Title);
        Assert.AreEqual("Ferien NRW", entries.Single().SourceName);
    }

    [TestMethod]
    public async Task Sync_ReplacesImportedEntries()
    {
        _fetcher.Content = Calendar(AllDay("a", "Alt", "20261012"));
        var source = await _service.CreateSourceAsync(_userId, new SaveHolidaySourceDto { Name = "Q", Url = "https://example.org/q.ics" });

        _fetcher.Content = Calendar(AllDay("b", "Neu 1", "20261013"), AllDay("c", "Neu 2", "20261014"));
        var synced = await _service.SyncSourceAsync(source.Id);

        Assert.AreEqual(2, synced.EntryCount);
        var titles = (await _service.GetEntriesAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))).Select(e => e.Title).ToArray();
        CollectionAssert.AreEquivalent(new[] { "Neu 1", "Neu 2" }, titles);
    }

    [TestMethod]
    public async Task Sync_Failure_KeepsEntriesAndStoresError()
    {
        _fetcher.Content = Calendar(AllDay("a", "Bleibt", "20261012"));
        var source = await _service.CreateSourceAsync(_userId, new SaveHolidaySourceDto { Name = "Q", Url = "https://example.org/q.ics" });

        _fetcher.Error = new HolidayFeedException("Der Server antwortete mit Status 404.");
        var failed = await _service.SyncSourceAsync(source.Id);

        Assert.AreEqual("Der Server antwortete mit Status 404.", failed.LastSyncError);
        Assert.AreEqual(1, failed.EntryCount);

        _fetcher.Error = null;
        _fetcher.Content = "kein kalender";
        failed = await _service.SyncSourceAsync(source.Id);
        Assert.AreEqual("Die Datei ist kein gültiger iCal-Kalender.", failed.LastSyncError);
        Assert.AreEqual(1, failed.EntryCount);
    }

    [TestMethod]
    public async Task DisabledSource_IsHiddenAndNotSynchronized()
    {
        _fetcher.Content = Calendar(AllDay("a", "Ferien", "20261012"));
        var source = await _service.CreateSourceAsync(_userId, new SaveHolidaySourceDto { Name = "Q", Url = "https://example.org/q.ics" });

        await _service.UpdateSourceAsync(_userId, source.Id, new SaveHolidaySourceDto { Name = "Q", Url = source.Url, IsEnabled = false });
        _fetcher.Calls = 0;
        await _service.SyncAllAsync();

        Assert.AreEqual(0, _fetcher.Calls);
        Assert.IsEmpty(await _service.GetEntriesAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
    }

    [TestMethod]
    public async Task DeleteSource_RemovesImportedEntries()
    {
        _fetcher.Content = Calendar(AllDay("a", "Ferien", "20261012"));
        var source = await _service.CreateSourceAsync(_userId, new SaveHolidaySourceDto { Name = "Q", Url = "https://example.org/q.ics" });

        await _service.DeleteSourceAsync(_userId, source.Id);

        Assert.AreEqual(0, await _db.HolidayEntries.CountAsync());
    }

    [TestMethod]
    public async Task ManualEntries_CanBeMaintained_ImportedOnesCannot()
    {
        var manual = await _service.CreateEntryAsync(_userId, new SaveHolidayEntryDto { Title = " Kirmes ", StartDate = new(2026, 8, 22), EndDate = new(2026, 8, 24) });
        Assert.AreEqual("Kirmes", manual.Title);

        var updated = await _service.UpdateEntryAsync(_userId, manual.Id, new SaveHolidayEntryDto { Title = "Kirmes", StartDate = new(2026, 8, 21), EndDate = new(2026, 8, 24) });
        Assert.AreEqual(new DateOnly(2026, 8, 21), updated.StartDate);

        _fetcher.Content = Calendar(AllDay("a", "Ferien", "20261012"));
        await _service.CreateSourceAsync(_userId, new SaveHolidaySourceDto { Name = "Q", Url = "https://example.org/q.ics" });
        var imported = await _db.HolidayEntries.SingleAsync(e => e.SourceId != null);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _service.DeleteEntryAsync(_userId, imported.Id));
        await _service.DeleteEntryAsync(_userId, manual.Id);
        Assert.AreEqual(1, await _db.HolidayEntries.CountAsync());
    }

    [TestMethod]
    public async Task CreateEntry_RejectsEndBeforeStart()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _service.CreateEntryAsync(
            _userId, new SaveHolidayEntryDto { Title = "X", StartDate = new(2026, 8, 22), EndDate = new(2026, 8, 21) }));
    }

    [TestMethod]
    public async Task GetEntries_ReturnsOverlappingEntriesOnly()
    {
        await _service.CreateEntryAsync(_userId, new SaveHolidayEntryDto { Title = "Davor", StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30) });
        await _service.CreateEntryAsync(_userId, new SaveHolidayEntryDto { Title = "Überlappt", StartDate = new(2026, 9, 28), EndDate = new(2026, 10, 2) });
        await _service.CreateEntryAsync(_userId, new SaveHolidayEntryDto { Title = "Danach", StartDate = new(2026, 11, 1), EndDate = new(2026, 11, 1) });

        var entries = await _service.GetEntriesAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.AreEqual("Überlappt", entries.Single().Title);
    }

    private sealed class FakeFetcher : IHolidayFeedFetcher
    {
        public string Content { get; set; } = Calendar();
        public Exception? Error { get; set; }
        public int Calls { get; set; }

        public Task<string> FetchAsync(Uri url, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Error is null ? Task.FromResult(Content) : Task.FromException<string>(Error);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
