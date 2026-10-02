namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     The join a many-to-many resolves to, once both of its declarations have been read.
/// </summary>
/// <remarks>
///     <para>
///         A many-to-many is symmetric: neither end owns it, so the join table and its two key columns
///         may be written on either end — but they describe one join, and there is only one of it.
///         While each end read its own declaration, two ends naming different tables produced
///         <b>two</b> join tables for one relationship, and keys written on one end never reached the
///         configuration generated from the other.
///     </para>
///     <para>
///         The values are picked in a canonical order — by the two entity names, not by which end is
///         asking — so both ends reach the same answer regardless of the order the graph walked them
///         in. Two ends naming the same thing differently is <c>PRAG0619</c>: there is no honest
///         precedence between them.
///     </para>
/// </remarks>
internal sealed record ResolvedManyToMany(
    string? JoinTable,
    string? JoinEntityTypeName,
    string? LeftKey,
    string? RightKey);
