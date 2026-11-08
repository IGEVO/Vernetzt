using System.Text;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Makes user-controlled values safe for log messages: control and line-separator characters are
/// replaced so a value cannot start a forged log line, and overly long values are truncated.
/// </summary>
public static class LogSanitizer
{
    public const int MaxLength = 256;

    public static string Clean(string? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        var builder = new StringBuilder(Math.Min(value.Length, MaxLength) + 1);
        foreach (var c in value)
        {
            if (builder.Length == MaxLength)
            {
                builder.Append('…');
                break;
            }

            builder.Append(char.IsControl(c) || c is '\u2028' or '\u2029' ? '_' : c);
        }

        // Explicit Replace calls make the sanitization visible to static analysis (CodeQL cs/log-forging).
        return builder.ToString().Replace("\r", "_").Replace("\n", "_");
    }
}
