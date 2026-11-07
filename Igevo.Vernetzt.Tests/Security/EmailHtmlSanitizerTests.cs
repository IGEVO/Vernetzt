using Igevo.Vernetzt.Server.Security;

namespace Igevo.Vernetzt.Tests.Security;

[TestClass]
public class EmailHtmlSanitizerTests
{
    [TestMethod]
    public void Sanitize_StripsExecutableMarkupAndKeepsFormatting()
    {
        var sanitizer = EmailHtmlSanitizerFactory.Create();
        var result = sanitizer.Sanitize("<p onclick='bad()'>Hallo <strong>Welt</strong></p><script>alert(1)</script><img src=x>");
        Assert.AreEqual("<p>Hallo <strong>Welt</strong></p>", result);
    }

    [TestMethod]
    public void Sanitize_AllowsMailAndWebLinksButRejectsJavascript()
    {
        var sanitizer = EmailHtmlSanitizerFactory.Create();
        var result = sanitizer.Sanitize("<a href='mailto:test@example.org'>Mail</a><a href='https://example.org'>Web</a><a href='javascript:alert(1)'>Bad</a>");
        StringAssert.Contains(result, "mailto:test@example.org");
        StringAssert.Contains(result, "https://example.org");
        Assert.IsFalse(result.Contains("javascript:", StringComparison.OrdinalIgnoreCase));
    }
}
