using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Synchronizes the "used locations" of a club, department or group with a list of location ids.
/// </summary>
internal static class LocationLinks
{
    /// <summary>
    /// Replaces the content of <paramref name="current"/> with the given locations.
    /// Does nothing when <paramref name="locationIds"/> is null (field not sent).
    /// Throws <see cref="InvalidOperationException"/> for unknown ids.
    /// </summary>
    public static async Task SyncAsync(
        VernetztDbContext context,
        ICollection<Location> current,
        IReadOnlyCollection<Guid>? locationIds,
        CancellationToken cancellationToken)
    {
        if (locationIds == null)
        {
            return;
        }

        var wanted = locationIds.Where(id => id != Guid.Empty).ToHashSet();
        var locations = wanted.Count == 0
            ? []
            : await context.Locations.Where(l => wanted.Contains(l.Id)).ToListAsync(cancellationToken);

        if (locations.Count != wanted.Count)
        {
            throw new InvalidOperationException("Mindestens ein ausgewählter Standort existiert nicht mehr.");
        }

        foreach (var removed in current.Where(l => !wanted.Contains(l.Id)).ToList())
        {
            current.Remove(removed);
        }

        foreach (var added in locations.Where(l => current.All(c => c.Id != l.Id)))
        {
            current.Add(added);
        }
    }

    /// <summary>
    /// Comparable audit representation (sorted names), null when there are none.
    /// </summary>
    public static string? FormatForAudit(IEnumerable<Location>? locations)
    {
        var names = (locations ?? []).Select(l => l.Name).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        return names.Count == 0 ? null : string.Join("; ", names);
    }
}
