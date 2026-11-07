namespace Igevo.Vernetzt.Core.Enums;

/// <summary>
/// Defines the scope of a calendar feed token.
/// </summary>
public enum CalendarFeedScope
{
    /// <summary>
    /// Feed for one club.
    /// </summary>
    Club = 0,

    /// <summary>
    /// Feed for one department.
    /// </summary>
    Department = 1,

    /// <summary>
    /// Global feed for Vernetzt.
    /// </summary>
    Global = 2
}
