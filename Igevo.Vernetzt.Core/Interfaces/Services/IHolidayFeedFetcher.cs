namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Downloads external iCal feeds for the holiday calendar.
/// </summary>
public interface IHolidayFeedFetcher
{
    /// <summary>
    /// Returns the feed content. Throws <see cref="HttpRequestException"/> when the feed cannot be
    /// loaded (unreachable, error status, too large or a forbidden target address).
    /// </summary>
    Task<string> FetchAsync(Uri url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Feed download failure with a message that can be shown to administrators as is.
/// </summary>
public sealed class HolidayFeedException(string message, Exception? innerException = null)
    : HttpRequestException(message, innerException);
