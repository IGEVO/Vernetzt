namespace Igevo.Vernetzt.Core.Enums;

/// <summary>
/// Views a media item is explicitly assigned to. Each usage is held by at most one media item per event;
/// views without explicit assignment fall back to the title image.
/// </summary>
[Flags]
public enum EventMediaUsage
{
    None = 0,

    /// <summary>Square icon in lists (1:1).</summary>
    List = 1,

    /// <summary>Wide header in the event detail view (2:1).</summary>
    Header = 2,

    /// <summary>Card in the public event overview (3:2).</summary>
    Card = 4
}
