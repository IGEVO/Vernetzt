using System.Text;

namespace Igevo.Vernetzt.Infrastructure.Services.CardDav;

/// <summary>
/// Writes a single vCard 3.0 (RFC 2426): escaping of text values, structured values
/// (N, ADR, ORG), lists (CATEGORIES) and line folding at 75 octets with CRLF.
/// vCard 3.0 instead of 4.0 because iOS/macOS Contacts handle it most reliably.
/// </summary>
internal sealed class VCardBuilder
{
    private const int MaxLineOctets = 75;
    private readonly StringBuilder _content = new();

    public VCardBuilder()
    {
        AppendLine("BEGIN:VCARD");
        AppendLine("VERSION:3.0");
    }

    /// <summary>Adds a property with a text value (escaped). Empty values are skipped.</summary>
    public VCardBuilder AddText(string name, string? value, string? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        return AddRaw(name, EscapeText(value.Trim()), parameters);
    }

    /// <summary>
    /// Adds a structured property (components separated by ';', each component escaped).
    /// Skipped if all components are empty unless <paramref name="allowEmpty"/> is set (N is mandatory).
    /// </summary>
    public VCardBuilder AddStructured(string name, IEnumerable<string?> components, string? parameters = null, bool allowEmpty = false)
    {
        var parts = components.Select(c => EscapeText(c?.Trim() ?? string.Empty)).ToList();
        if (!allowEmpty && parts.All(p => p.Length == 0)) return this;
        return AddRaw(name, string.Join(';', parts), parameters);
    }

    /// <summary>Adds a list property (values separated by ',', each value escaped). Skipped if empty.</summary>
    public VCardBuilder AddList(string name, IEnumerable<string?> values, string? parameters = null)
    {
        var parts = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => EscapeText(v!.Trim()))
            .ToList();
        if (parts.Count == 0) return this;
        return AddRaw(name, string.Join(',', parts), parameters);
    }

    /// <summary>Adds an inline photo (base64, RFC 2426 "ENCODING=b").</summary>
    public VCardBuilder AddPhoto(byte[]? jpeg)
    {
        if (jpeg is null || jpeg.Length == 0) return this;
        return AddRaw("PHOTO", Convert.ToBase64String(jpeg), "ENCODING=b;TYPE=JPEG");
    }

    /// <summary>Adds a property with an already encoded value (e.g. a URI).</summary>
    public VCardBuilder AddRaw(string name, string value, string? parameters = null)
    {
        var line = string.IsNullOrEmpty(parameters) ? $"{name}:{value}" : $"{name};{parameters}:{value}";
        AppendLine(line);
        return this;
    }

    public string Build()
    {
        AppendLine("END:VCARD");
        return _content.ToString();
    }

    /// <summary>
    /// Escapes a text value: backslash, comma, semicolon and line breaks (RFC 2426 section 4).
    /// </summary>
    internal static string EscapeText(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case ',': sb.Append("\\,"); break;
                case ';': sb.Append("\\;"); break;
                case '\r':
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    sb.Append("\\n");
                    break;
                case '\n': sb.Append("\\n"); break;
                default:
                    // Other control characters are not allowed in vCard values.
                    if (!char.IsControl(c)) sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private void AppendLine(string line)
    {
        _content.Append(Fold(line)).Append("\r\n");
    }

    /// <summary>
    /// Folds a content line at 75 octets (UTF-8) without splitting a character; continuation lines
    /// start with a single space, which counts towards their length.
    /// </summary>
    internal static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= MaxLineOctets) return line;

        var sb = new StringBuilder(line.Length + line.Length / MaxLineOctets * 3);
        var octets = 0;
        var limit = MaxLineOctets;
        var index = 0;
        while (index < line.Length)
        {
            var charLength = char.IsSurrogatePair(line, index) ? 2 : 1;
            var charOctets = Encoding.UTF8.GetByteCount(line.AsSpan(index, charLength));
            if (octets + charOctets > limit)
            {
                sb.Append("\r\n ");
                octets = 0;
                limit = MaxLineOctets - 1;
            }

            sb.Append(line, index, charLength);
            octets += charOctets;
            index += charLength;
        }

        return sb.ToString();
    }
}
