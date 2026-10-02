namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     The name of a generated many-to-many join table, when the author does not give one.
/// </summary>
/// <remarks>
///     <para>
///         The twin of <see cref="JoinColumnNaming" />, and here for the same reason: the entity
///         configuration tells EF Core what to look for and the schema metadata tells the migration
///         what to create, so the name has to come from one place.
///     </para>
///     <para>
///         ⚠️ Without an explicit <c>JoinTable</c>, leaving the name to EF Core's own convention would
///         split the two: the schema builder skips a table with no name, so EF would query a table no
///         migration created — <c>42P01: relation "LabelOrder" does not exist</c>, on the first write
///         through the navigation.
///     </para>
///     <para>
///         The rule follows EF Core's own — the two entity names in ordinal order, concatenated — so
///         a database created by that convention is still the one described.
///     </para>
/// </remarks>
internal static class JoinTableNaming
{
    /// <param name="declaringTypeName">Simple name of the entity carrying the navigation.</param>
    /// <param name="targetTypeName">Simple name of the entity at the other end.</param>
    /// <param name="declared">The author's own name, when they gave one.</param>
    public static string For(string declaringTypeName, string targetTypeName, string? declared)
    {
        if (!string.IsNullOrEmpty(declared))
            return declared!;

        return string.CompareOrdinal(declaringTypeName, targetTypeName) <= 0
            ? declaringTypeName + targetTypeName
            : targetTypeName + declaringTypeName;
    }
}
