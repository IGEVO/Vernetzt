using Igevo.Vernetzt.Infrastructure.Services;

namespace Igevo.Vernetzt.Tests.Services;

/// <summary>
/// Log values from user input must not be able to forge log lines, and e-mail addresses must only appear masked.
/// </summary>
[TestClass]
public class LogSanitizerTests
{
    [TestMethod]
    [DataRow("avatars/users/a.png", "avatars/users/a.png")]
    [DataRow("evil\r\n[INF] forged entry", "evil__[INF] forged entry")]
    [DataRow("tab\there", "tab_here")]
    [DataRow("line\u2028sep", "line_sep")]
    public void Clean_ReplacesControlCharacters(string input, string expected)
    {
        Assert.AreEqual(expected, LogSanitizer.Clean(input));
    }

    [TestMethod]
    public void Clean_Null_ReturnsEmptyMarker()
    {
        Assert.AreEqual("<null>", LogSanitizer.Clean(null));
    }

    [TestMethod]
    public void Clean_TruncatesVeryLongValues()
    {
        var cleaned = LogSanitizer.Clean(new string('x', 1000));

        Assert.IsTrue(cleaned.Length <= LogSanitizer.MaxLength + 1);
        Assert.IsTrue(cleaned.EndsWith('…'));
    }

    [TestMethod]
    [DataRow("user@example.com", "u***@example.com")]
    [DataRow("a@example.com", "a***@example.com")]
    [DataRow("", "<empty>")]
    [DataRow("no-at-sign", "***")]
    [DataRow("x\r\n@evil.com", "x***@evil.com")]
    [DataRow("x@evil.com\r\nforged", "x***@evil.com__forged")]
    public void Mask_MasksLocalPartAndStripsControlCharacters(string input, string expected)
    {
        Assert.AreEqual(expected, EmailAddressHelper.Mask(input));
    }
}
