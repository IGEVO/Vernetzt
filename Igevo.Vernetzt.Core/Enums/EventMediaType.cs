namespace Igevo.Vernetzt.Core.Enums;

public enum EventMediaType
{
    /// <summary>Explicitly chosen title image (at most one per event).</summary>
    Cover = 0,

    /// <summary>Poster, usually A4 portrait (at most one per event, image only).</summary>
    Poster = 1,

    /// <summary>Document attachment (e.g. PDF flyer, registration form).</summary>
    Attachment = 2,

    /// <summary>Other image (neither title image nor poster).</summary>
    Image = 3
}
