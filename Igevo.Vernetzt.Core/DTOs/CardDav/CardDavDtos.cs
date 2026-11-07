namespace Igevo.Vernetzt.Core.DTOs.CardDav;

/// <summary>
/// State of the personal CardDAV access (never contains the password).
/// </summary>
public class CardDavStatusDto
{
    public bool HasAccess { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Server URL to enter in the client (ends with /carddav/).</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Username for the client: the user's email address.</summary>
    public string Username { get; set; } = string.Empty;

    public bool IncludeOrganizations { get; set; }
}

/// <summary>
/// Result of creating a CardDAV access: the password is only returned this one time.
/// </summary>
public class CardDavAccessCreatedDto
{
    public string Password { get; set; } = string.Empty;
    public CardDavStatusDto Status { get; set; } = new();
}

public class UpdateCardDavSettingsDto
{
    public bool IncludeOrganizations { get; set; }
}

/// <summary>
/// One address object in the CardDAV collection.
/// </summary>
/// <param name="ResourceName">File name in the collection, e.g. "{id}.vcf".</param>
/// <param name="ETag">Quoted entity tag.</param>
/// <param name="Content">The vCard text.</param>
public sealed record CardDavCard(string ResourceName, string ETag, string Content);

/// <summary>
/// The complete address book of one user.
/// </summary>
/// <param name="CTag">Collection tag; changes whenever any card changes, is added or removed.</param>
public sealed record CardDavAddressBook(string CTag, IReadOnlyList<CardDavCard> Cards)
{
    public CardDavCard? Find(string resourceName) =>
        Cards.FirstOrDefault(c => string.Equals(c.ResourceName, resourceName, StringComparison.OrdinalIgnoreCase));
}
