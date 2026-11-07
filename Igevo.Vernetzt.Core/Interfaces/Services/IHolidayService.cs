using Igevo.Vernetzt.Core.DTOs.Holidays;

namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Central holiday calendar (school holidays, public holidays): manual entries and external iCal
/// subscriptions. Validation errors are reported as <see cref="InvalidOperationException"/>,
/// unknown ids as <see cref="KeyNotFoundException"/>.
/// </summary>
public interface IHolidayService
{
    /// <summary>Entries of enabled sources and manual entries overlapping the given days (inclusive).</summary>
    Task<IReadOnlyList<HolidayEntryDto>> GetEntriesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<HolidayEntryDto> CreateEntryAsync(Guid userId, SaveHolidayEntryDto dto, CancellationToken cancellationToken = default);
    Task<HolidayEntryDto> UpdateEntryAsync(Guid userId, Guid id, SaveHolidayEntryDto dto, CancellationToken cancellationToken = default);
    Task DeleteEntryAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HolidaySourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the subscription and synchronizes it right away (a failed sync is reported in the result).</summary>
    Task<HolidaySourceDto> CreateSourceAsync(Guid userId, SaveHolidaySourceDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates the subscription; a changed URL is synchronized right away.</summary>
    Task<HolidaySourceDto> UpdateSourceAsync(Guid userId, Guid id, SaveHolidaySourceDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes the subscription together with its imported entries.</summary>
    Task DeleteSourceAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Synchronizes one subscription. Errors are stored on the source, existing entries are kept.</summary>
    Task<HolidaySourceDto> SyncSourceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Synchronizes all enabled subscriptions (background job).</summary>
    Task SyncAllAsync(CancellationToken cancellationToken = default);
}
