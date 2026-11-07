namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for processing images (resizing, format conversion, optimization).
/// </summary>
public interface IImageProcessingService
{
    /// <summary>
    /// Processes an uploaded avatar image and generates multiple sizes.
    /// </summary>
    /// <param name="inputStream">The input image stream.</param>
    /// <param name="originalFileName">The original file name (for format detection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of size name to processed image data.</returns>
    Task<ImageProcessingResult> ProcessAvatarAsync(Stream inputStream, string originalFileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the supported input image formats.
    /// </summary>
    IReadOnlyList<string> SupportedInputFormats { get; }

    /// <summary>
    /// Validates if a file is a supported image format.
    /// </summary>
    /// <param name="fileName">The file name to validate.</param>
    /// <returns>True if the format is supported.</returns>
    bool IsSupportedFormat(string fileName);

    /// <summary>
    /// Validates and normalizes an event media image upload (JPEG, PNG, WebP, HEIC) by inspecting its content.
    /// Images are auto-oriented, stripped of metadata and capped in size; responsive WebP previews are generated.
    /// </summary>
    Task<EventMediaProcessingResult> ProcessEventMediaAsync(Stream inputStream, string originalFileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts an already stored image (e.g. a WebP avatar) into a small JPEG, scaled to fit
    /// <paramref name="maxEdge"/> and flattened onto white (JPEG has no transparency).
    /// Used where clients do not support WebP, e.g. vCard photos. Returns null if the data cannot be read.
    /// </summary>
    byte[]? ConvertToJpeg(byte[] imageData, int maxEdge, int quality = 80);

    /// <summary>
    /// Processes the logo of the public portal: keeps the aspect ratio (no crop), fits it into a
    /// square bounding box and converts it to WebP with transparency. The result holds one image "logo".
    /// </summary>
    Task<ImageProcessingResult> ProcessPortalLogoAsync(Stream inputStream, string originalFileName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of processing an event media upload.
/// </summary>
public class EventMediaProcessingResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>The normalized image to store as "original".</summary>
    public byte[] Data { get; init; } = [];
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>File extension including the dot (e.g. ".jpg").</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>Preview dimensions (null if no preview could be generated).</summary>
    public int? Width { get; init; }
    public int? Height { get; init; }

    /// <summary>WebP previews keyed by pixel width.</summary>
    public IReadOnlyList<ProcessedImage> Variants { get; init; } = [];
}

/// <summary>
/// Result of image processing containing multiple sizes.
/// </summary>
public class ImageProcessingResult
{
    /// <summary>
    /// Whether the processing was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The processed images in different sizes.
    /// Key: size name (thumb, small, medium, original), Value: image data.
    /// </summary>
    public Dictionary<string, ProcessedImage> Images { get; init; } = [];
}

/// <summary>
/// A processed image with its metadata.
/// </summary>
public class ProcessedImage
{
    /// <summary>
    /// The image data as bytes.
    /// </summary>
    public byte[] Data { get; init; } = [];

    /// <summary>
    /// The MIME content type.
    /// </summary>
    public string ContentType { get; init; } = "image/webp";

    /// <summary>
    /// The file extension (without dot).
    /// </summary>
    public string Extension { get; init; } = "webp";

    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public int Height { get; init; }
}
