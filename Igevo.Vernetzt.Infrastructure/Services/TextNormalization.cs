namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Normalization helpers for free-text input.
/// </summary>
public static class TextNormalization
{
    /// <summary>
    /// Trims the value and returns null for empty or whitespace-only input.
    /// </summary>
    public static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
