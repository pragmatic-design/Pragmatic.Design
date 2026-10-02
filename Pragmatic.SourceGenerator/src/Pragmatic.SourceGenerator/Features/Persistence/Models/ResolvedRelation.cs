namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     What one relationship resolves to, once both of its declarations have been read.
/// </summary>
/// <remarks>
///     <para>
///         A relationship may be declared from the parent (<c>OneToMany</c>), from the child
///         (<c>ManyToOne</c>), or from both. Each declaration describes the same single relationship,
///         and each option belongs to the side that owns what it describes: the delete behaviour to
///         the parent, because it describes the relationship; the column's name and nullability to
///         the child, because that is where the column lives.
///     </para>
///     <para>
///         Resolving once is what makes the two ends agree. While each declaration derived its own
///         answer, the members were reconciled by <em>member name</em> — but a relationship's identity
///         is not a member name, so two declarations naming things differently became two
///         relationships (two columns, two navigations), and two naming them the same lost whichever
///         was processed second.
///     </para>
/// </remarks>
internal sealed record ResolvedRelation(
    string ChildNavigationName,
    string ForeignKeyName,
    bool IsRequired,
    string OnDelete);
