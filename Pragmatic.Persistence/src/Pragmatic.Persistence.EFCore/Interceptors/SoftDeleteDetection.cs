using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     Shared detection of soft-delete transitions on tracked entries. A soft delete reaches EF as a
///     <c>Modified</c> entry on an <see cref="ISoftDelete"/> entity whose <c>IsDeleted</c> flag flips —
///     interceptors that treat "delete" semantically (audit log, roll-up) must map it accordingly.
///     Gating on <see cref="ISoftDelete"/> + <c>nameof</c> keeps this from misfiring on an unrelated
///     "IsDeleted" column and from drifting if the interface member is ever renamed.
/// </summary>
internal static class SoftDeleteDetection
{
    /// <summary>The entry is a Modified <see cref="ISoftDelete"/> whose flag was just set to true.</summary>
    public static bool IsSoftDeleting(EntityEntry entry)
        => FindFlag(entry) is { IsModified: true, CurrentValue: true };

    /// <summary>The entry is a Modified <see cref="ISoftDelete"/> whose flag was just cleared (restore).</summary>
    public static bool IsSoftRestoring(EntityEntry entry)
        => FindFlag(entry) is { IsModified: true, CurrentValue: false, OriginalValue: true };

    /// <summary>The entity is currently flagged deleted (independent of whether the flag just changed).</summary>
    public static bool IsFlaggedDeleted(EntityEntry entry)
        => entry.Entity is ISoftDelete { IsDeleted: true };

    private static PropertyEntry? FindFlag(EntityEntry entry)
    {
        if (entry.Entity is not ISoftDelete)
            return null;

        foreach (var property in entry.Properties)
            if (property.Metadata.Name == nameof(ISoftDelete.IsDeleted))
                return property;

        return null;
    }
}
