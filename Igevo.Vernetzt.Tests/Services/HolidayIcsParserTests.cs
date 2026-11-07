using System.Net;
using Igevo.Vernetzt.Infrastructure.Services;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class HolidayIcsParserTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateOnly WindowStart = new(2025, 1, 1);
    private static readonly DateOnly WindowEnd = new(2027, 12, 31);

    internal static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//DE\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    internal static string AllDay(string uid, string summary, string start, string? endExclusive = null, string extra = "") =>
        $"BEGIN:VEVENT\r\nUID:{uid}\r\nDTSTART;VALUE=DATE:{start}\r\n" +
        (endExclusive is null ? string.Empty : $"DTEND;VALUE=DATE:{endExclusive}\r\n") +
        $"SUMMARY:{summary}\r\n{extra}END:VEVENT\r\n";

    [TestMethod]
    public void Parse_MultiDayAllDay_TreatsDtEndAsExclusive()
    {
        var result = HolidayIcsParser.Parse(
            Calendar(AllDay("h1", "Herbstferien", "20261012", "20261025")), WindowStart, WindowEnd, Berlin);

        Assert.HasCount(1, result);
        Assert.AreEqual("Herbstferien", result[0].Title);
        Assert.AreEqual(new DateOnly(2026, 10, 12), result[0].StartDate);
        Assert.AreEqual(new DateOnly(2026, 10, 24), result[0].EndDate);
    }

    [TestMethod]
    public void Parse_SingleDayWithoutEnd_IsOneDay()
    {
        var result = HolidayIcsParser.Parse(
            Calendar(AllDay("h1", "Tag der Deutschen Einheit", "20261003")), WindowStart, WindowEnd, Berlin);

        Assert.AreEqual(new DateOnly(2026, 10, 3), result.Single().StartDate);
        Assert.AreEqual(new DateOnly(2026, 10, 3), result.Single().EndDate);
    }

    [TestMethod]
    public void Parse_YearlyRecurrence_IsExpandedWithinWindowOnly()
    {
        var result = HolidayIcsParser.Parse(
            Calendar(AllDay("xmas", "1. Weihnachtstag", "20101225", "20101226", "RRULE:FREQ=YEARLY\r\n")),
            WindowStart, WindowEnd, Berlin);

        CollectionAssert.AreEqual(
            new[] { new DateOnly(2025, 12, 25), new DateOnly(2026, 12, 25), new DateOnly(2027, 12, 25) },
            result.Select(r => r.StartDate).ToArray());
        Assert.AreEqual(3, result.Select(r => r.ExternalUid).Distinct().Count(), "Occurrences need distinct ids.");
    }

    [TestMethod]
    public void Parse_TimedEvent_UsesBerlinCalendarDays()
    {
        // 23:30 UTC on 31 Dec is already 1 Jan in Berlin.
        var ics = Calendar("BEGIN:VEVENT\r\nUID:t1\r\nDTSTART:20251231T233000Z\r\nDTEND:20260101T010000Z\r\nSUMMARY:Neujahr\r\nEND:VEVENT\r\n");

        var result = HolidayIcsParser.Parse(ics, WindowStart, WindowEnd, Berlin).Single();

        Assert.AreEqual(new DateOnly(2026, 1, 1), result.StartDate);
        Assert.AreEqual(new DateOnly(2026, 1, 1), result.EndDate);
    }

    [TestMethod]
    public void Parse_SkipsCancelledUntitledAndOutOfWindowEvents()
    {
        var ics = Calendar(
            AllDay("c", "Abgesagt", "20260501", extra: "STATUS:CANCELLED\r\n"),
            AllDay("o", "Zu alt", "20200101"),
            "BEGIN:VEVENT\r\nUID:n\r\nDTSTART;VALUE=DATE:20260601\r\nEND:VEVENT\r\n",
            AllDay("k", "Pfingstferien", "20260526", "20260527"));

        var result = HolidayIcsParser.Parse(ics, WindowStart, WindowEnd, Berlin);

        Assert.AreEqual("Pfingstferien", result.Single().Title);
    }

    [TestMethod]
    public void Parse_FoldedLines_AreUnfolded()
    {
        var ics = Calendar("BEGIN:VEVENT\r\nUID:f\r\nDTSTART;VALUE=DATE:20260406\r\nSUMMARY:Oster\r\n ferien\r\nEND:VEVENT\r\n");

        Assert.AreEqual("Osterferien", HolidayIcsParser.Parse(ics, WindowStart, WindowEnd, Berlin).Single().Title);
    }

    [TestMethod]
    [DataRow("<html>Not found</html>")]
    [DataRow("")]
    public void Parse_InvalidContent_ThrowsFormatException(string content)
    {
        Assert.ThrowsExactly<FormatException>(() => HolidayIcsParser.Parse(content, WindowStart, WindowEnd, Berlin));
    }

    [TestMethod]
    public void Parse_DailyRecurrence_IsCapped()
    {
        var ics = Calendar(AllDay("d", "Täglich", "20250101", "20250102", "RRULE:FREQ=DAILY\r\n"));
        var wide = new DateOnly(2035, 12, 31);

        var ex = Assert.ThrowsExactly<FormatException>(() => HolidayIcsParser.Parse(ics, WindowStart, wide, Berlin));
        StringAssert.Contains(ex.Message, HolidayIcsParser.MaxEntries.ToString());
    }

    [TestMethod]
    [DataRow("127.0.0.1", false)]
    [DataRow("10.1.2.3", false)]
    [DataRow("172.20.0.1", false)]
    [DataRow("192.168.1.10", false)]
    [DataRow("169.254.169.254", false)]
    [DataRow("100.64.0.1", false)]
    [DataRow("0.0.0.0", false)]
    [DataRow("::1", false)]
    [DataRow("fe80::1", false)]
    [DataRow("fd00::1", false)]
    [DataRow("::ffff:10.0.0.1", false)]
    [DataRow("93.184.216.34", true)]
    [DataRow("2a00:1450:4001:82a::200e", true)]
    public void FeedFetcher_IsPublicAddress(string address, bool expected)
    {
        Assert.AreEqual(expected, HolidayFeedFetcher.IsPublicAddress(IPAddress.Parse(address)));
    }

    [TestMethod]
    [DataRow("webcal://example.org/ferien.ics", "https://example.org/ferien.ics")]
    [DataRow("  https://example.org/a.ics ", "https://example.org/a.ics")]
    public void NormalizeUrl_AcceptsHttpsAndWebcal(string input, string expected)
    {
        Assert.AreEqual(expected, HolidayService.NormalizeUrl(input));
    }

    [TestMethod]
    [DataRow("http://example.org/a.ics")]
    [DataRow("file:///etc/passwd")]
    [DataRow("ferien.ics")]
    [DataRow("https://user:pw@example.org/a.ics")]
    public void NormalizeUrl_RejectsOtherUrls(string input)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => HolidayService.NormalizeUrl(input));
    }
}
