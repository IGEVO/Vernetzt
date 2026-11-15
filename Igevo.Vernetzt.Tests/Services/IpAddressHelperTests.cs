using System.Net;
using Igevo.Vernetzt.Infrastructure.Services;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class IpAddressHelperTests
{
    [TestMethod]
    [DataRow("203.0.113.57", "203.0.113.0")]
    [DataRow("::ffff:198.51.100.23", "198.51.100.0")]
    [DataRow("2001:db8:85a3:8d3:1319:8a2e:370:7348", "2001:db8:85a3::")]
    public void Anonymize_ShortensAddress(string input, string expected)
    {
        Assert.AreEqual(expected, IpAddressHelper.Anonymize(IPAddress.Parse(input)));
    }

    [TestMethod]
    public void Anonymize_Null_ReturnsUnknown()
    {
        Assert.AreEqual("unknown", IpAddressHelper.Anonymize(null));
    }
}
