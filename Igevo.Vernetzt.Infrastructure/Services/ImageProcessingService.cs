using Igevo.Vernetzt.Core.Interfaces.Services;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Image processing service using Magick.NET.
/// Supports JPEG, PNG, WebP and HEIC input formats (detected by magic bytes) and converts to WebP for output.
/// </summary>
public class ImageProcessingService : IImageProcessingService
{
    private readonly ILogger<ImageProcessingService> _logger;

    static ImageProcessingService()
    {
        // Process-wide upper bounds for ImageMagick, in addition to the per-image 40 MP check.
        ResourceLimits.Width = 16_384;
        ResourceLimits.Height = 16_384;
        ResourceLimits.Area = 128_000_000;
        ResourceLimits.Memory = 512UL * 1024 * 1024;
        ResourceLimits.Disk = 1024UL * 1024 * 1024;
        ResourceLimits.Time = 60;
    }

    /// <summary>
    /// Avatar size definitions.
    /// </summary>
    private static readonly Dictionary<string, int> AvatarSizes = new()
    {
        { "thumb", 64 },
        { "small", 128 },
        { "medium", 256 },
        { "original", 512 }
    };

    /// <summary>
    /// Supported input image extensions.
    /// </summary>
    private static readonly string[] SupportedExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif"
    ];

    public ImageProcessingService(ILogger<ImageProcessingService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> SupportedInputFormats => SupportedExtensions;

    public bool IsSupportedFormat(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public async Task<ImageProcessingResult> ProcessAvatarAsync(Stream inputStream, string originalFileName, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate format
            if (!IsSupportedFormat(originalFileName))
            {
                return new ImageProcessingResult
                {
                    Success = false,
                    ErrorMessage = $"Unsupported image format. Supported formats: {string.Join(", ", SupportedExtensions)}"
                };
            }

            // Detect the real format from the content and pin it, so Magick.NET never auto-detects
            // other coders (SVG, PostScript, PDF, ...) regardless of the file name.
            var data = ReadAllBytes(inputStream);
            var format = DetectFormat(data);
            if (format is null)
            {
                return new ImageProcessingResult
                {
                    Success = false,
                    ErrorMessage = $"Unsupported image format. Supported formats: {string.Join(", ", SupportedExtensions)}"
                };
            }

            // Do not decode implausibly large images, even if the upload byte limit was met.
            var settings = new MagickReadSettings { Format = format.Value };
            if (ExceedsPixelLimit(data, settings))
            {
                return new ImageProcessingResult
                {
                    Success = false,
                    ErrorMessage = "Das Bild ist zu groß. Maximal 40 Megapixel sind erlaubt."
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var image = new MagickImage(data, settings);
            cancellationToken.ThrowIfCancellationRequested();

            image.AutoOrient();
            image.Strip();

            _logger.LogInformation("Processing avatar image: {Width}x{Height}, Format: {Format}",
                image.Width, image.Height, originalFileName);

            var results = new Dictionary<string, ProcessedImage>();

            // Process each size
            foreach (var (sizeName, targetSize) in AvatarSizes)
            {
                var processedImage = await ProcessSizeAsync(image, sizeName, targetSize, cancellationToken);
                results[sizeName] = processedImage;
            }

            _logger.LogInformation("Successfully processed avatar in {Count} sizes", results.Count);

            return new ImageProcessingResult
            {
                Success = true,
                Images = results
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MagickException ex)
        {
            _logger.LogWarning(ex, "Invalid image content: {FileName}", originalFileName);
            return new ImageProcessingResult
            {
                Success = false,
                ErrorMessage = "Die Bilddatei ist beschädigt oder ungültig."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing avatar image: {FileName}", originalFileName);
            return new ImageProcessingResult
            {
                Success = false,
                ErrorMessage = "Fehler bei der Bildverarbeitung. Bitte versuchen Sie es mit einem anderen Bild."
            };
        }
    }

    /// <summary>
    /// Preview widths generated for event media (list thumbnails, cards, detail header, lightbox).
    /// </summary>
    private static readonly int[] EventMediaVariantWidths = [160, 480, 960, 1600];

    /// <summary>
    /// Longest edge of the stored (normalized) original image.
    /// </summary>
    private const int EventMediaMaxEdge = 2560;

    private const ulong MaximumPixels = 40_000_000;

    public Task<EventMediaProcessingResult> ProcessEventMediaAsync(Stream inputStream, string originalFileName, CancellationToken cancellationToken = default)
    {
        var data = ReadAllBytes(inputStream);
        var format = DetectFormat(data);
        if (format == null)
        {
            return Task.FromResult(Fail("Dateityp wird nicht unterstützt. Erlaubt sind JPG, PNG, WebP und HEIC."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ProcessImage(data, format.Value, originalFileName, cancellationToken));
    }

    private EventMediaProcessingResult ProcessImage(byte[] data, MagickFormat format, string originalFileName, CancellationToken cancellationToken)
    {
        try
        {
            var settings = new MagickReadSettings { Format = format };
            if (ExceedsPixelLimit(data, settings))
            {
                return Fail("Das Bild ist zu groß. Maximal 40 Megapixel sind erlaubt.");
            }

            using var image = new MagickImage(data, settings);

            image.AutoOrient();
            image.Strip();

            if (image.Width > EventMediaMaxEdge || image.Height > EventMediaMaxEdge)
            {
                var (w, h) = CalculateResizeDimensions((int)image.Width, (int)image.Height, EventMediaMaxEdge);
                image.FilterType = FilterType.Lanczos;
                image.Resize((uint)w, (uint)h);
            }

            // Normalize the stored original: keep PNG/WebP (may contain transparency), everything else becomes JPEG.
            var (outFormat, contentType, extension) = format switch
            {
                MagickFormat.Png => (MagickFormat.Png, "image/png", ".png"),
                MagickFormat.WebP => (MagickFormat.WebP, "image/webp", ".webp"),
                _ => (MagickFormat.Jpeg, "image/jpeg", ".jpg")
            };

            cancellationToken.ThrowIfCancellationRequested();
            var variants = CreateVariants(image, cancellationToken);

            image.Format = outFormat;
            if (outFormat != MagickFormat.Png)
            {
                image.Quality = 88;
            }

            return new EventMediaProcessingResult
            {
                Success = true,
                Data = image.ToByteArray(),
                ContentType = contentType,
                Extension = extension,
                Width = (int)image.Width,
                Height = (int)image.Height,
                Variants = variants
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MagickException ex)
        {
            _logger.LogWarning(ex, "Invalid event media image: {FileName}", originalFileName);
            return Fail("Die Bilddatei ist beschädigt oder ungültig.");
        }
    }

    /// <summary>
    /// Bounding box of the portal logo (square or portrait logos next to the start page heading;
    /// roughly three times the largest display size, for sharp rendering on HiDPI screens).
    /// </summary>
    private const int PortalLogoMaxWidth = 480;
    private const int PortalLogoMaxHeight = 480;

    public Task<ImageProcessingResult> ProcessPortalLogoAsync(Stream inputStream, string originalFileName, CancellationToken cancellationToken = default)
    {
        var data = ReadAllBytes(inputStream);
        var format = DetectFormat(data);
        if (format == null)
        {
            return Task.FromResult(new ImageProcessingResult
            {
                Success = false,
                ErrorMessage = "Dateityp wird nicht unterstützt. Erlaubt sind JPG, PNG, WebP und HEIC."
            });
        }

        try
        {
            var settings = new MagickReadSettings { Format = format.Value };
            if (ExceedsPixelLimit(data, settings))
            {
                return Task.FromResult(new ImageProcessingResult
                {
                    Success = false,
                    ErrorMessage = "Das Bild ist zu groß. Maximal 40 Megapixel sind erlaubt."
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var image = new MagickImage(data, settings);
            image.AutoOrient();
            image.Strip();

            var scale = Math.Min(1d, Math.Min(PortalLogoMaxWidth / (double)image.Width, PortalLogoMaxHeight / (double)image.Height));
            if (scale < 1d)
            {
                image.FilterType = FilterType.Lanczos;
                image.Resize(
                    (uint)Math.Max(1, Math.Round(image.Width * scale)),
                    (uint)Math.Max(1, Math.Round(image.Height * scale)));
            }

            cancellationToken.ThrowIfCancellationRequested();
            image.Format = MagickFormat.WebP;
            image.Quality = 90;

            return Task.FromResult(new ImageProcessingResult
            {
                Success = true,
                Images = new Dictionary<string, ProcessedImage>
                {
                    ["logo"] = new()
                    {
                        Data = image.ToByteArray(),
                        Width = (int)image.Width,
                        Height = (int)image.Height
                    }
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MagickException ex)
        {
            _logger.LogWarning(ex, "Invalid portal logo image: {FileName}", originalFileName);
            return Task.FromResult(new ImageProcessingResult
            {
                Success = false,
                ErrorMessage = "Die Bilddatei ist beschädigt oder ungültig."
            });
        }
    }

    private static List<ProcessedImage> CreateVariants(MagickImage source, CancellationToken cancellationToken)
    {
        var result = new List<ProcessedImage>();
        foreach (var targetWidth in EventMediaVariantWidths.Select(w => Math.Min(w, (int)source.Width)).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var clone = new MagickImage(source);
            if (clone.Width > targetWidth)
            {
                var targetHeight = Math.Max(1, (int)Math.Round(source.Height * (double)targetWidth / source.Width));
                clone.FilterType = FilterType.Lanczos;
                clone.Resize((uint)targetWidth, (uint)targetHeight);
            }

            clone.Format = MagickFormat.WebP;
            clone.Quality = targetWidth <= 160 ? 72u : 82u;
            result.Add(new ProcessedImage
            {
                Data = clone.ToByteArray(),
                ContentType = "image/webp",
                Extension = "webp",
                Width = (int)clone.Width,
                Height = (int)clone.Height
            });
        }

        return result;
    }

    /// <summary>
    /// Detects the file type by its magic bytes. Only whitelisted formats are returned so that
    /// Magick.NET never auto-detects exotic (and potentially dangerous) coders.
    /// </summary>
    internal static MagickFormat? DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return null;
        }

        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return MagickFormat.Jpeg;
        }

        if (data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return MagickFormat.Png;
        }

        if (data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return MagickFormat.WebP;
        }

        if (data[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = System.Text.Encoding.ASCII.GetString(data[8..12]);
            if (brand is "heic" or "heix" or "hevc" or "heim" or "heis" or "mif1" or "msf1")
            {
                return MagickFormat.Heic;
            }
        }

        return null;
    }

    private static EventMediaProcessingResult Fail(string message) => new() { Success = false, ErrorMessage = message };

    private static byte[] ReadAllBytes(Stream inputStream)
    {
        if (inputStream is MemoryStream ms && ms.TryGetBuffer(out var segment) && segment.Offset == 0 && segment.Count == ms.Length)
        {
            return segment.Array!.Length == segment.Count ? segment.Array : segment.ToArray();
        }

        using var copy = new MemoryStream();
        inputStream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Reads only the image header (no pixel allocation) and checks the 40 MP limit.
    /// </summary>
    /// <inheritdoc />
    public byte[]? ConvertToJpeg(byte[] imageData, int maxEdge, int quality = 80)
    {
        try
        {
            using var image = new MagickImage(imageData);
            var (width, height) = CalculateResizeDimensions((int)image.Width, (int)image.Height, maxEdge);
            image.FilterType = FilterType.Lanczos;
            image.Resize((uint)width, (uint)height);
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
            image.Strip();
            image.Format = MagickFormat.Jpeg;
            image.Quality = (uint)Math.Clamp(quality, 1, 100);
            return image.ToByteArray();
        }
        catch (MagickException ex)
        {
            _logger.LogWarning(ex, "Could not convert image to JPEG");
            return null;
        }
    }

    private static bool ExceedsPixelLimit(byte[] data, MagickReadSettings settings)
    {
        var info = new MagickImageInfo(data, settings);
        return (ulong)info.Width * info.Height > MaximumPixels;
    }

    private Task<ProcessedImage> ProcessSizeAsync(MagickImage image, string sizeName, int targetSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var clone = new MagickImage(image);
        var (newWidth, newHeight) = CalculateResizeDimensions((int)image.Width, (int)image.Height, targetSize);
        clone.FilterType = FilterType.Lanczos;
        clone.Resize((uint)newWidth, (uint)newHeight);

        if (sizeName != "original")
        {
            var size = Math.Min(clone.Width, clone.Height);
            clone.Crop(size, size, Gravity.Center);
        }

        clone.Format = MagickFormat.WebP;
        clone.Quality = sizeName == "thumb" ? 70u : 85u;
        var result = new ProcessedImage
        {
            Data = clone.ToByteArray(),
            ContentType = "image/webp",
            Extension = "webp",
            Width = (int)clone.Width,
            Height = (int)clone.Height
        };
        return Task.FromResult(result);
    }

    private static (int width, int height) CalculateResizeDimensions(int originalWidth, int originalHeight, int maxSize)
    {
        if (originalWidth <= maxSize && originalHeight <= maxSize)
        {
            return (originalWidth, originalHeight);
        }

        double ratio;
        if (originalWidth > originalHeight)
        {
            ratio = (double)maxSize / originalWidth;
        }
        else
        {
            ratio = (double)maxSize / originalHeight;
        }

        return ((int)(originalWidth * ratio), (int)(originalHeight * ratio));
    }
}
