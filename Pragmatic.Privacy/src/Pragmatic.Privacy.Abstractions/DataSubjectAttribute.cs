namespace Pragmatic.Privacy;

/// <summary>
///     Marks an entity as a data subject — a person the rest of the personal data hangs off.
/// </summary>
/// <remarks>
///     <para>
///         Declaring one is what switches the privacy analysis on. Until an application has a subject,
///         the generator emits nothing and reports nothing: there is no graph to walk, and warning about
///         unclassified strings in a codebase that has not opted in would be noise nobody can act on.
///     </para>
///     <para>
///         From the first <c>[DataSubject]</c>, the blast radius is exactly the set of entities
///         reachable from it — not the whole solution.
///     </para>
/// </remarks>
/// <param name="identifierProperty">
///     The property holding the subject's stable identifier. This is what an erasure request is resolved
///     against.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DataSubjectAttribute(string identifierProperty) : Attribute
{
    /// <summary>The property holding the subject's stable identifier.</summary>
    public string IdentifierProperty { get; } = identifierProperty;
}
