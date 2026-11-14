using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Converts an iCal feed into day ranges for the holiday calendar. Recurring events (e.g. fixed public
/// holidays with RRULE) are expanded within the requested window; times are reduced to Berlin calendar days.
/// </summary>
internal static class HolidayIcsParser
{
    internal sealed record ParsedHoliday(string Title, DateOnly StartDate, DateOnly EndDate, string ExternalUid);

    /// <summary>Upper bound per feed; protects against endless recurrence rules and oversized feeds.</summary>
    internal const int MaxEntries = 2000;

    /// <summary>
    /// Upper bound for evaluated occurrences, including those that are filtered out (cancelled, untitled,
    /// duplicates per day). Bounds the work, not only the output.
    /// </summary>
    internal const int MaxEvaluatedOccurrences = 20000;

    private const int MaxTitleLength = 200;
    private const int MaxUidLength = 500;

    /// <summary>
    /// Parses the feed. Throws <see cref="FormatException"/> for content that is not a valid iCal calendar
    /// or that would take too much work to expand.
    /// </summary>
    internal static IReadOnlyList<ParsedHoliday> Parse(
        string content,
        DateOnly windowStart,
        DateOnly windowEnd,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken = default)
    {
        Calendar? calendar;
        try
        {
            calendar = Calendar.Load(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new FormatException("Die Datei ist kein gültiger iCal-Kalender.", ex);
        }

        if (calendar is null)
        {
            throw new FormatException("Die Datei ist kein gültiger iCal-Kalender.");
        }

        // Only calendar days are stored; sub-daily rules would only multiply the expansion work
        if (calendar.Events.Any(e =>
                e.RecurrenceRule?.Frequency is FrequencyType.Secondly or FrequencyType.Minutely or FrequencyType.Hourly))
        {
            throw new FormatException("Der Kalender enthält Wiederholungen in Abständen unter einem Tag; das wird nicht unterstützt.");
        }

        var result = new List<ParsedHoliday>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = calendar
            .GetOccurrences(new CalDateTime(windowStart), new EvaluationOptions())
            .TakeWhileBefore(new CalDateTime(windowEnd.AddDays(1)));

        var evaluated = 0;
        foreach (var occurrence in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++evaluated > MaxEvaluatedOccurrences)
            {
                throw new FormatException($"Der Kalender enthält mehr als {MaxEvaluatedOccurrences} Termine im Zeitraum.");
            }

            if (occurrence.Source is not CalendarEvent calendarEvent)
            {
                continue;
            }

            if (string.Equals(calendarEvent.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = calendarEvent.Summary?.Trim();
            if (string.IsNullOrEmpty(title))
            {
                continue;
            }

            var (start, end) = ToDays(occurrence.Period, timeZone);
            if (end < windowStart || start > windowEnd)
            {
                continue;
            }

            var uid = $"{calendarEvent.Uid ?? title}@{start:yyyy-MM-dd}";
            if (!seen.Add(uid))
            {
                continue;
            }

            if (result.Count >= MaxEntries)
            {
                throw new FormatException($"Der Kalender enthält mehr als {MaxEntries} Einträge im Zeitraum.");
            }

            result.Add(new ParsedHoliday(Truncate(title, MaxTitleLength), start, end, Truncate(uid, MaxUidLength)));
        }

        return result;
    }

    /// <summary>
    /// All-day: DTEND is exclusive, so the last day is the day before. Timed: the Berlin days touched by the range.
    /// </summary>
    private static (DateOnly Start, DateOnly End) ToDays(Period period, TimeZoneInfo timeZone)
    {
        var startTime = period.StartTime;
        var endTime = period.EffectiveEndTime;

        if (!startTime.HasTime)
        {
            var startDay = startTime.Date;
            var endExclusive = endTime?.Date ?? startDay.AddDays(1);
            var endDay = endExclusive > startDay ? endExclusive.AddDays(-1) : startDay;
            return (startDay, endDay);
        }

        var localStart = ToLocal(startTime, timeZone);
        var localEnd = endTime is null ? localStart : ToLocal(endTime, timeZone);
        // An end at exactly midnight belongs to the previous day.
        if (localEnd > localStart)
        {
            localEnd = localEnd.AddTicks(-1);
        }

        return (DateOnly.FromDateTime(localStart), DateOnly.FromDateTime(localEnd < localStart ? localStart : localEnd));
    }

    private static DateTime ToLocal(CalDateTime value, TimeZoneInfo timeZone) =>
        value.IsFloating
            ? value.Value
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc), timeZone);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
