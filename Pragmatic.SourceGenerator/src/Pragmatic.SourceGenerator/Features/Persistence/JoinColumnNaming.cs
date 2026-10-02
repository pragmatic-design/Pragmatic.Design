namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     The two foreign-key column names of a generated many-to-many join table.
/// </summary>
/// <remarks>
///     <para>
///         One place, because two generators write about the same table: the entity configuration
///         tells EF Core what to look for, and the schema metadata tells the migration what to create.
///         If they disagree, the table exists with columns the model never asks about, and nothing
///         fails until the first write through the navigation.
///     </para>
///     <para>
///         ⚠️ On a self-reference the entity's own name cannot name both ends: two
///         <c>KnowledgeItemId</c> columns would make the primary key repeat a column, PostgreSQL
///         refuses such a table outright, and the host would not start. There the navigations name them, which is
///         also the only thing that distinguishes the two ends to a reader.
///     </para>
/// </remarks>
internal static class JoinColumnNaming
{
    /// <summary>
    ///     The column naming the declaring side, and the one naming the target side.
    /// </summary>
    /// <param name="declaringTypeName">Simple name of the entity carrying the navigation.</param>
    /// <param name="targetTypeName">Simple name of the entity at the other end.</param>
    /// <param name="navigationName">The collection on the declaring side.</param>
    /// <param name="inverseName">The collection on the other side, when one is declared.</param>
    public static (string Left, string Right) For(
        string declaringTypeName,
        string targetTypeName,
        string navigationName,
        string? inverseName)
    {
        if (!string.Equals(declaringTypeName, targetTypeName, System.StringComparison.Ordinal))
            return (declaringTypeName + "Id", targetTypeName + "Id");

        // Both ends are the same entity. The inverse is mandatory here — PRAG0613 refuses the
        // declaration without one — so it is the name of the far side; the near side keeps the
        // navigation's own name.
        var inverse = string.IsNullOrEmpty(inverseName) ? "Inverse" : inverseName!;

        return (inverse + "Id", navigationName + "Id");
    }
}
