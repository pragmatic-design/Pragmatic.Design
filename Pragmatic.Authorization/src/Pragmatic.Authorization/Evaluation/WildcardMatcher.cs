namespace Pragmatic.Authorization.Evaluation;

/// <summary>
///     Matches permission patterns with multi-level wildcard support.
///     Supports:
///     <list type="bullet">
///         <item><c>booking.*</c> — all permissions in boundary</item>
///         <item><c>booking.reservation.*</c> — all operations on entity</item>
///         <item><c>booking.*.read</c> — read on all entities in boundary</item>
///         <item><c>*.reservation.read</c> — read on Reservation across all boundaries</item>
///         <item><c>*</c> — all permissions (super admin)</item>
///     </list>
/// </summary>
internal static class WildcardMatcher
{
    /// <summary>
    ///     Expands wildcard patterns against a known set of permissions.
    ///     Returns a new set containing all matched concrete permissions.
    /// </summary>
    public static IReadOnlySet<string> ExpandWildcards(
        IEnumerable<string> patterns, IReadOnlySet<string> allKnownPermissions)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in patterns)
        {
            if (pattern == "*")
            {
                // Super admin: all permissions
                foreach (var known in allKnownPermissions)
                    result.Add(known);
            }
            else if (pattern.Contains("*"))
            {
                // Wildcard pattern — match against all known permissions
                foreach (var known in allKnownPermissions)
                {
                    if (Matches(pattern, known))
                        result.Add(known);
                }
            }
            else
            {
                result.Add(pattern);
            }
        }

        return result;
    }

    /// <summary>
    ///     Checks if a single pattern matches a specific permission.
    ///     Supports <c>*</c> at any segment position.
    /// </summary>
    public static bool Matches(string pattern, string permission)
    {
        if (string.Equals(pattern, permission, StringComparison.OrdinalIgnoreCase))
            return true;

        if (pattern == "*")
            return true;

        if (!pattern.Contains("*"))
            return false;

        // Segment-level matching. A trailing '*' matches the whole remaining subtree (any depth,
        // including zero extra segments); a '*' in any other position matches exactly one segment:
        // - "booking.*"              matches "booking" AND "booking.reservation" AND "booking.a.b.c"
        //                            (trailing-* branch — the whole subtree under "booking")
        // - "booking.reservations.*" matches "booking.reservations", "booking.reservations.read",
        //                            and "booking.reservations.a.b" (same trailing-* subtree rule)
        // - "booking.*.read"         matches "booking.reservation.read" (mid '*' = one segment)
        // - "*.reservation.read"     matches "any.reservation.read"
        // Grant semantics are "you hold this node and everything beneath it", so trailing-* covering
        // the bare prefix and any depth is intentional.
        return MatchSegments(
            pattern.Split('.'),
            permission.Split('.'));
    }

    /// <summary>
    ///     Segment-level matching. A non-trailing <c>*</c> matches exactly one segment; a trailing
    ///     <c>*</c> matches the whole remaining subtree (any depth, including none).
    /// </summary>
    private static bool MatchSegments(string[] patternParts, string[] permissionParts)
    {
        if (patternParts.Length != permissionParts.Length)
        {
            // Special case: trailing * matches all remaining segments
            if (patternParts.Length > 0 && patternParts[patternParts.Length - 1] == "*")
                return permissionParts.Length >= patternParts.Length - 1
                       && MatchPrefix(patternParts, permissionParts, patternParts.Length - 1);

            return false;
        }

        for (var i = 0; i < patternParts.Length; i++)
        {
            if (patternParts[i] == "*")
                continue; // wildcard matches any single segment

            if (!string.Equals(patternParts[i], permissionParts[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool MatchPrefix(string[] patternParts, string[] permissionParts, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (patternParts[i] == "*")
                continue;

            if (i >= permissionParts.Length)
                return false;

            if (!string.Equals(patternParts[i], permissionParts[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
