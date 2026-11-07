using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Services;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class EventMediaProcessingTests
{
    private ImageProcessingService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new ImageProcessingService(NullLogger<ImageProcessingService>.Instance);
    }

    private static byte[] CreateImage(uint width, uint height, MagickFormat format)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, width, height);
        image.Format = format;
        return image.ToByteArray();
    }

    [TestMethod]
    public async Task PortraitJpeg_IsNormalizedAndGetsVariants()
    {
        var data = CreateImage(1200, 1700, MagickFormat.Jpeg);

        var result = await _service.ProcessEventMediaAsync(new MemoryStream(data), "plakat.jpeg");

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual("image/jpeg", result.ContentType);
        Assert.AreEqual(".jpg", result.Extension);
        Assert.AreEqual(1200, result.Width);
        Assert.AreEqual(1700, result.Height);
        CollectionAssert.AreEqual(new[] { 160, 480, 960, 1200 }, result.Variants.Select(v => v.Width).ToArray());
        Assert.IsTrue(result.Variants.All(v => v.ContentType == "image/webp"));
        // Aspect ratio is preserved in previews (no cropping)
        var smallest = result.Variants[0];
        Assert.AreEqual(Math.Round(160 * 1700 / 1200.0), smallest.Height, 1);
    }

    [TestMethod]
    public async Task HugeImage_IsCappedToMaxEdge()
    {
        var data = CreateImage(4000, 3000, MagickFormat.Png);

        var result = await _service.ProcessEventMediaAsync(new MemoryStream(data), "foto.png");

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual("image/png", result.ContentType);
        Assert.AreEqual(2560, result.Width);
        Assert.AreEqual(1920, result.Height);
        Assert.AreEqual(1600, result.Variants[^1].Width);
    }

    [TestMethod]
    public async Task ContentIsDetectedByMagicBytes_NotByExtension()
    {
        var data = CreateImage(300, 300, MagickFormat.Png);

        var result = await _service.ProcessEventMediaAsync(new MemoryStream(data), "tarnung.pdf");

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual("image/png", result.ContentType);
    }

    [TestMethod]
    public async Task UnknownContent_IsRejected()
    {
        var data = "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8.ToArray();

        var result = await _service.ProcessEventMediaAsync(new MemoryStream(data), "bild.jpg");

        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task Pdf_IsRejected()
    {
        var data = "%PDF-1.4\n%âã\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();

        var result = await _service.ProcessEventMediaAsync(new MemoryStream(data), "flyer.pdf");

        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task Avatar_Png_IsProcessed()
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 600, 400) { Format = MagickFormat.Png };

        var result = await _service.ProcessAvatarAsync(new MemoryStream(image.ToByteArray()), "avatar.png");

        Assert.IsTrue(result.Success, result.ErrorMessage);
    }

    [TestMethod]
    [DataRow("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100000\" height=\"100000\"></svg>", "avatar.png")]
    [DataRow("%!PS-Adobe-3.0\nshowpage\n", "avatar.jpg")]
    [DataRow("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF", "avatar.jpg")]
    public async Task Avatar_NonRasterContent_IsRejectedBeforeDecoding(string content, string fileName)
    {
        var result = await _service.ProcessAvatarAsync(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(content)), fileName);

        Assert.IsFalse(result.Success);
    }
}
