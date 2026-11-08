using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace Igevo.Vernetzt.Infrastructure.Services;

public static class EmailAddressHelper
{
    public static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();

        try
        {
            var mail = new MailAddress(trimmed);
            var atIndex = mail.Address.LastIndexOf('@');
            if (atIndex <= 0 || atIndex == mail.Address.Length - 1)
            {
                return mail.Address.ToLowerInvariant();
            }

            var local = mail.Address[..atIndex];
            var domain = mail.Address[(atIndex + 1)..];
            var asciiDomain = new IdnMapping().GetAscii(domain);
            return $"{local}@{asciiDomain}".ToLowerInvariant();
        }
        catch
        {
            return trimmed.ToLowerInvariant();
        }
    }

    public static string Sha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "<empty>";
        }

        var parts = email.Split('@');
        if (parts.Length != 2 || parts[0].Length == 0)
        {
            return "***";
        }

        return LogSanitizer.Clean($"{parts[0][0]}***@{parts[1]}");
    }

    public static (string Type, string Label, string Reason) BuildBadge(bool isContextUsed, bool hasContextEmail, bool contextVerified)
    {
        if (isContextUsed)
        {
            return ("ContextVerified", "Amtsadresse", "Die bestätigte Amtsadresse dieser Zuordnung wird verwendet.");
        }

        if (hasContextEmail && !contextVerified)
        {
            return ("ContextUnverifiedHidden", "Persönliche Adresse", "Die Amtsadresse ist noch nicht bestätigt, daher wird die persönliche Adresse genutzt.");
        }

        return ("PrimaryFallback", "Persönliche Adresse", "Keine bestätigte Amtsadresse vorhanden, daher wird die persönliche Adresse genutzt.");
    }
}
