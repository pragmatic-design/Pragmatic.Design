using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Compositions;

/// <summary>
///     Looks a constant path, as written in a <c>[RequirePermission]</c> argument, up in the catalog
///     built by <see cref="PermissionCatalogBuilder" />. Shared by the two consumers, Actions and
///     Endpoints: two copies of this matching would drift, and a path that stopped matching fails open.
/// </summary>
internal static class PermissionCatalogLookup
{
    /// <summary>Indexes the catalog for repeated lookups. Build once per resolution pass.</summary>
    public static Dictionary<string, string> Index(EquatableArray<PermissionConstEntry> catalog)
    {
        var index = new Dictionary<string, string>();
        foreach (var entry in catalog)
            index[entry.ConstPath] = entry.Value;
        return index;
    }

    /// <summary>
    ///     Resolves a constant reference to its permission value. Tries an exact match, then
    ///     progressively shorter trailing segments so a namespace-qualified reference (e.g.
    ///     <c>Showcase.Auth.BookingPermissions.GuestPreferences.Update</c>) still resolves.
    /// </summary>
    public static string? Resolve(string path, Dictionary<string, string> index)
    {
        if (index.TryGetValue(path, out var exact))
            return exact;

        var segments = path.Split('.');
        // Catalog keys are 2 ({Class}.Op) or 3 ({Class}.Entity.Op) segments — try 3 then 2.
        for (var take = 3; take >= 2; take--)
        {
            if (segments.Length < take)
                continue;
            var suffix = string.Join(".", segments, segments.Length - take, take);
            if (index.TryGetValue(suffix, out var value))
                return value;
        }

        return null;
    }
}
