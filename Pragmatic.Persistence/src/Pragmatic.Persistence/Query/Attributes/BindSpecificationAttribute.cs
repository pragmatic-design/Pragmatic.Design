namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Declares that this input is read by a specification on the same query, rather than by a filter
///     the generator writes.
/// </summary>
/// <remarks>
///     <para>
///         A query property becomes a filter when it carries <c>[Filter]</c>, when it is
///         <c>required</c>, or when it is nullable. An input that exists only to build a
///         <see cref="Pragmatic.Specification.Specification{T}" /> is none of those — it names no column and the generator has
///         nothing to compare — so without this attribute it looks exactly like a filter somebody forgot
///         to declare, and <c>PRAG0707</c> refuses it.
///     </para>
///     <para>
///         A <see cref="Pragmatic.Specification.Specification{T}" /> cannot be bound from a request — there is no way to
///         deserialize a predicate — so the specification itself is a computed, get-only property. What
///         crosses the wire is this: the value it reads.
///     </para>
///     <example>
///         <code>
/// [Query&lt;KnowledgeItem, KnowledgeItemDto&gt;]
/// public partial class SuggestTermsQuery
/// {
///     [BindSpecification]
///     public bool ConfirmedOnly { get; init; }
///
///     public Specification&lt;KnowledgeItem&gt;? Confirmed
///         =&gt; ConfirmedOnly ? KnowledgeSpecs.Confirmed() : null;
/// }
/// </code>
///     </example>
///     <para>
///         ⚠️ The attribute is a claim, and <c>PRAG0709</c> checks the half of it that can be checked: a
///         query with an input marked this way and no <see cref="Pragmatic.Specification.Specification{T}" /> property at all is
///         an error. Marking an input and then deleting the specification would otherwise leave the
///         value read and dropped, with a declaration standing over it saying otherwise — a quieter
///         silence than the one this attribute exists to lift.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class BindSpecificationAttribute : Attribute;
