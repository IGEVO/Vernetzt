namespace Igevo.Vernetzt.Core.Enums;

/// <summary>
/// Social media platforms a club or department can link to.
/// Values are persisted (JSON) and exposed via the API, so never renumber existing entries.
/// </summary>
public enum SocialMediaPlatform
{
    Facebook = 0,
    Instagram = 1,
    YouTube = 2,
    TikTok = 3,
    X = 4,
    Mastodon = 5,
    Bluesky = 6,
    Threads = 7,
    WhatsApp = 8
}
