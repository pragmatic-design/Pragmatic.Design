namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Why a <c>[Query]</c> written on a member derived nothing.
/// </summary>
/// <remarks>
///     ⚠️ Six <c>return null;</c> in one transform would all look the same to the caller: the
///     attribute compiles, the generator produces no query, and the build is green — the shape the
///     silent-drops ratchet exists to count.
///     <para>
///         Every path carries a reason, including the ones that cannot happen. A guard that fires
///         when it was believed unreachable is exactly the case worth hearing about, and reporting it
///         costs one enum member.
///     </para>
/// </remarks>
internal enum DerivedQueryRejection
{
    /// <summary>Nothing was rejected.</summary>
    None = 0,

    /// <summary>The attribute is on something that is neither a method nor a property.</summary>
    NotAMember,

    /// <summary>The member is not static, so there is no rule to read without an instance.</summary>
    NotStatic,

    /// <summary><c>Pragmatic.Specification</c> is not referenced, so no member can be one.</summary>
    SpecificationTypeMissing,

    /// <summary>The member does not return a <c>Specification&lt;TEntity&gt;</c>.</summary>
    NotASpecification,

    /// <summary>The attribute names no usable result type.</summary>
    NoResultType,

    /// <summary>The declaring type is generic, so the derived query would need type parameters.</summary>
    GenericContainer
}
