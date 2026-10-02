using Pragmatic.Privacy;

namespace Pragmatic.Privacy.Tests.Wiring;

/// <summary>
///     The data subject of the wiring suite: the person an access or erasure request is about.
/// </summary>
/// <remarks>
///     Every string property is classified on purpose. PRAG2903 is an error on anything reachable from
///     a subject, and leaving one unclassified here would be testing the diagnostic rather than the
///     wiring.
/// </remarks>
[DataSubject(nameof(Email))]
public class Customer
{
    /// <summary>Primary key. Not the subject reference — that one is opaque and lives in the registry.</summary>
    public int Id { get; set; }

    /// <summary>What the registry resolves a subject reference back to.</summary>
    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Retain,
                  Reason = "identifies the subject; clearing it would orphan every other row")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    ///     Cleared on erasure — and therefore nullable.
    /// </summary>
    /// <remarks>
    ///     <c>ErasureStrategy.Null</c> on a non-nullable property is a plan the database rejects: EF maps
    ///     it to a NOT NULL column and the erasure fails on SaveChanges. Declaring the field for what it
    ///     is, is the fix; the generator cannot see the column.
    /// </remarks>
    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Null)]
    public string? FullName { get; set; }

    /// <summary>The rows that reach the subject through this navigation.</summary>
    public List<Order> Orders { get; } = [];
}
