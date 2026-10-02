using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Entity information extraction helpers for DbContextResultExtensions.
/// </summary>
public static partial class DbContextResultExtensions
{
    private static object? GetEntityId(EntityEntry? entry)
    {
        if (entry is null)
            return null;

        try
        {
            var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
            if (keyProperties is null || keyProperties.Count == 0)
                return null;

            if (keyProperties.Count == 1)
                return entry.Property(keyProperties[0].Name).CurrentValue;

            // Composite key - return as anonymous object string
            var keyValues = keyProperties
                .Select(p => $"{p.Name}={entry.Property(p.Name).CurrentValue}")
                .ToArray();
            return string.Join(", ", keyValues);
        }
        catch
        {
            return null;
        }
    }

    private static EntityEntry? GetFirstEntry(IReadOnlyList<EntityEntry> entries)
    {
        return entries.Count > 0 ? entries[0] : null;
    }

    private static string GetEntityTypeFromException(DbUpdateException ex)
    {
        var entry = GetFirstEntry(ex.Entries);
        return entry?.Entity.GetType().Name ?? "Unknown";
    }
}
