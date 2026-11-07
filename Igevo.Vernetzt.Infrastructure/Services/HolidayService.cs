using System.Text.Json;
using Igevo.Vernetzt.Core.DTOs.Holidays;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Central holiday calendar: manual entries plus entries imported from external iCal subscriptions.
/// Holidays are a display layer only and are never part of the iCal exports (see <see cref="CalendarFeedService"/>).
/// </summary>
public sealed class HolidayService(
    VernetztDbContext context,
    IHolidayFeedFetcher feedFetcher,
    IAuditLogRepository auditLog,
    TimeProvider timeProvider,
    ILogger<HolidayService> logger) : IHolidayService
{
    /// <summary>Largest range a single calendar request may ask for.</summary>
    internal const int MaxRangeDays = 800;

    /// <summary>Imported window: one year back, two years ahead.</summary>
    internal const int ImportYearsBack = 1;
    internal const int ImportYearsAhead = 2;

    private static readonly TimeZoneInfo BerlinTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public async Task<IReadOnlyList<HolidayEntryDto>> GetEntriesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidOperationException("Das Ende des Zeitraums liegt vor dem Anfang.");
        }

        if (to.DayNumber - from.DayNumber > MaxRangeDays)
        {
            throw new InvalidOperationException($"Der Zeitraum darf höchstens {MaxRangeDays} Tage umfassen.");
        }

        return await context.HolidayEntries
            .AsNoTracking()
            .Where(e => e.StartDate <= to && e.EndDate >= from)
            .Where(e => e.SourceId == null || e.Source!.IsEnabled)
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.Title)
            .Select(e => new HolidayEntryDto
            {
                Id = e.Id,
                Title = e.Title,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                SourceId = e.SourceId,
                SourceName = e.Source != null ? e.Source.Name : null
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<HolidayEntryDto> CreateEntryAsync(Guid userId, SaveHolidayEntryDto dto, CancellationToken cancellationToken = default)
    {
        var entry = new HolidayEntry();
        Apply(entry, dto);
        context.HolidayEntries.Add(entry);
        await context.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(nameof(HolidayEntry), entry.Id, AuditAction.Create, userId, null, Describe(entry), cancellationToken);
        return MapEntry(entry, null);
    }

    public async Task<HolidayEntryDto> UpdateEntryAsync(Guid userId, Guid id, SaveHolidayEntryDto dto, CancellationToken cancellationToken = default)
    {
        var entry = await FindManualEntryAsync(id, cancellationToken);
        var before = Describe(entry);
        Apply(entry, dto);
        await context.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(nameof(HolidayEntry), entry.Id, AuditAction.Update, userId, before, Describe(entry), cancellationToken);
        return MapEntry(entry, null);
    }

    public async Task DeleteEntryAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await FindManualEntryAsync(id, cancellationToken);
        context.HolidayEntries.Remove(entry);
        await context.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(nameof(HolidayEntry), entry.Id, AuditAction.Delete, userId, Describe(entry), null, cancellationToken);
    }

    public async Task<IReadOnlyList<HolidaySourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
        await context.HolidaySources
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new HolidaySourceDto
            {
                Id = s.Id,
                Name = s.Name,
                Url = s.Url,
                IsEnabled = s.IsEnabled,
                LastSyncedAt = s.LastSyncedAt,
                LastSyncError = s.LastSyncError,
                EntryCount = s.Entries.Count
            })
            .ToListAsync(cancellationToken);

    public async Task<HolidaySourceDto> CreateSourceAsync(Guid userId, SaveHolidaySourceDto dto, CancellationToken cancellationToken = default)
    {
        var source = new HolidaySource();
        Apply(source, dto);
        context.HolidaySources.Add(source);
        await context.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(nameof(HolidaySource), source.Id, AuditAction.Create, userId, null, Describe(source), cancellationToken);

        return source.IsEnabled
            ? await SyncSourceAsync(source.Id, cancellationToken)
            : await GetSourceDtoAsync(source.Id, cancellationToken);
    }

    public async Task<HolidaySourceDto> UpdateSourceAsync(Guid userId, Guid id, SaveHolidaySourceDto dto, CancellationToken cancellationToken = default)
    {
        var source = await context.HolidaySources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();
        var before = Describe(source);
        var previousUrl = source.Url;
        var wasEnabled = source.IsEnabled;

        Apply(source, dto);
        await context.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(nameof(HolidaySource), source.Id, AuditAction.Update, userId, before, Describe(source), cancellationToken);

        var needsSync = source.IsEnabled && (source.Url != previousUrl || !wasEnabled);
        return needsSync
            ? await SyncSourceAsync(source.Id, cancellationToken)
            : await GetSourceDtoAsync(source.Id, cancellationToken);
    }

    public async Task DeleteSourceAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var source = await context.HolidaySources.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();

        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            await context.HolidayEntries.Where(e => e.SourceId == id).ExecuteDeleteAsync(cancellationToken);
            await context.HolidaySources.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        DetachEntries(e => e.SourceId == id);
        foreach (var tracked in context.ChangeTracker.Entries<HolidaySource>().Where(e => e.Entity.Id == id).ToList())
        {
            tracked.State = EntityState.Detached;
        }

        await auditLog.LogAsync(nameof(HolidaySource), source.Id, AuditAction.Delete, userId, Describe(source), null, cancellationToken);
    }

    public async Task<HolidaySourceDto> SyncSourceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await context.HolidaySources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();

        await SyncAsync(source, cancellationToken);
        return await GetSourceDtoAsync(id, cancellationToken);
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var sources = await context.HolidaySources
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        foreach (var source in sources)
        {
            await SyncAsync(source, cancellationToken);
        }
    }

    /// <summary>
    /// Downloads and parses the feed, then replaces the imported entries in one transaction.
    /// On failure the error is stored and the previous entries stay in place.
    /// </summary>
    private async Task SyncAsync(HolidaySource source, CancellationToken cancellationToken)
    {
        IReadOnlyList<HolidayIcsParser.ParsedHoliday> parsed;
        try
        {
            var content = await feedFetcher.FetchAsync(new Uri(source.Url), cancellationToken);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), BerlinTimeZone).DateTime);
            parsed = HolidayIcsParser.Parse(
                content,
                new DateOnly(today.Year - ImportYearsBack, 1, 1),
                new DateOnly(today.Year + ImportYearsAhead, 12, 31),
                BerlinTimeZone);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Holiday source {SourceId} could not be synchronized", source.Id);
            source.LastSyncError = DescribeError(ex);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.HolidayEntries.Where(e => e.SourceId == source.Id).ExecuteDeleteAsync(cancellationToken);
        DetachEntries(e => e.SourceId == source.Id);
        context.HolidayEntries.AddRange(parsed.Select(p => new HolidayEntry
        {
            Title = p.Title,
            StartDate = p.StartDate,
            EndDate = p.EndDate,
            ExternalUid = p.ExternalUid,
            SourceId = source.Id
        }));
        source.LastSyncedAt = timeProvider.GetUtcNow().UtcDateTime;
        source.LastSyncError = null;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Holiday source {SourceId} synchronized: {Count} entries", source.Id, parsed.Count);
    }

    /// <summary>
    /// Bulk deletes bypass the change tracker; drop entries it still holds so later saves don't touch them.
    /// </summary>
    private void DetachEntries(Func<HolidayEntry, bool> predicate)
    {
        foreach (var tracked in context.ChangeTracker.Entries<HolidayEntry>().Where(e => predicate(e.Entity)).ToList())
        {
            tracked.State = EntityState.Detached;
        }
    }

    internal static string DescribeError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is HolidayFeedException or FormatException)
            {
                return current.Message;
            }
        }

        return ex is TaskCanceledException or TimeoutException
            ? "Zeitüberschreitung beim Abrufen des Kalenders."
            : "Der Kalender konnte nicht abgerufen werden.";
    }

    /// <summary>
    /// Accepts http(s) and webcal links and normalizes them to HTTPS.
    /// </summary>
    internal static string NormalizeUrl(string raw)
    {
        var value = raw.Trim();
        if (value.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value["webcal://".Length..];
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host))
        {
            throw new InvalidOperationException("Bitte eine gültige HTTPS- oder webcal-Adresse angeben.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Zugangsdaten in der Adresse sind nicht erlaubt.");
        }

        return uri.AbsoluteUri;
    }

    private static void Apply(HolidayEntry entry, SaveHolidayEntryDto dto)
    {
        var title = dto.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            throw new InvalidOperationException("Bitte einen Namen angeben.");
        }

        if (dto.EndDate < dto.StartDate)
        {
            throw new InvalidOperationException("Das Ende liegt vor dem Anfang.");
        }

        if (dto.EndDate.DayNumber - dto.StartDate.DayNumber > 366)
        {
            throw new InvalidOperationException("Ein Eintrag darf höchstens ein Jahr umfassen.");
        }

        entry.Title = title.Length > 200 ? title[..200] : title;
        entry.StartDate = dto.StartDate;
        entry.EndDate = dto.EndDate;
    }

    private static void Apply(HolidaySource source, SaveHolidaySourceDto dto)
    {
        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Bitte einen Namen angeben.");
        }

        source.Name = name.Length > 200 ? name[..200] : name;
        source.Url = NormalizeUrl(dto.Url ?? string.Empty);
        source.IsEnabled = dto.IsEnabled;
    }

    private async Task<HolidayEntry> FindManualEntryAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await context.HolidayEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();
        if (entry.SourceId is not null)
        {
            throw new InvalidOperationException("Importierte Einträge können nur über ihr Abonnement geändert werden.");
        }

        return entry;
    }

    private async Task<HolidaySourceDto> GetSourceDtoAsync(Guid id, CancellationToken cancellationToken) =>
        (await GetSourcesAsync(cancellationToken)).First(s => s.Id == id);

    private static HolidayEntryDto MapEntry(HolidayEntry entry, string? sourceName) => new()
    {
        Id = entry.Id,
        Title = entry.Title,
        StartDate = entry.StartDate,
        EndDate = entry.EndDate,
        SourceId = entry.SourceId,
        SourceName = sourceName
    };

    private static string Describe(HolidayEntry entry) =>
        JsonSerializer.Serialize(new { entry.Title, entry.StartDate, entry.EndDate });

    private static string Describe(HolidaySource source) =>
        JsonSerializer.Serialize(new { source.Name, source.Url, source.IsEnabled });
}
