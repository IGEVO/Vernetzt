using System.Text;
using Igevo.Vernetzt.Infrastructure.Services.CardDav;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class VCardBuilderTests
{
    [TestMethod]
    public void EscapeText_EscapesSpecialCharacters()
    {
        Assert.AreEqual(@"a\\b\,c\;d\ne\nf", VCardBuilder.EscapeText("a\\b,c;d\r\ne\nf"));
    }

    [TestMethod]
    public void Build_WritesVersionAndCrlfLines()
    {
        var card = new VCardBuilder().AddText("FN", "Max Muster").Build();

        Assert.AreEqual("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Max Muster\r\nEND:VCARD\r\n", card);
    }

    [TestMethod]
    public void AddText_SkipsEmptyValues()
    {
        var card = new VCardBuilder().AddText("TEL", "  ").AddText("EMAIL", null).Build();

        Assert.IsFalse(card.Contains("TEL", StringComparison.Ordinal));
        Assert.IsFalse(card.Contains("EMAIL", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AddStructured_EscapesEachComponent()
    {
        var card = new VCardBuilder().AddStructured("ORG", ["TSV Musterstadt, SV Beispiel", "Fußball; Jugend"]).Build();

        StringAssert.Contains(card, @"ORG:TSV Musterstadt\, SV Beispiel;Fußball\; Jugend");
    }

    [TestMethod]
    public void Fold_SplitsLongLinesAt75OctetsWithoutBreakingCharacters()
    {
        var line = "NOTE:" + string.Concat(Enumerable.Repeat("äöü€", 40));

        var folded = VCardBuilder.Fold(line);
        var physicalLines = folded.Split("\r\n");

        Assert.IsTrue(physicalLines.Length > 1);
        foreach (var physical in physicalLines)
        {
            Assert.IsTrue(Encoding.UTF8.GetByteCount(physical) <= 75, $"Line too long: {physical}");
        }

        Assert.IsTrue(physicalLines.Skip(1).All(l => l.StartsWith(' ')));
        Assert.AreEqual(line, folded.Replace("\r\n ", string.Empty));
    }

    [TestMethod]
    public void AddPhoto_WritesBase64WithJpegType()
    {
        var card = new VCardBuilder().AddPhoto([1, 2, 3]).Build();

        StringAssert.Contains(card, "PHOTO;ENCODING=b;TYPE=JPEG:AQID");
    }
}
