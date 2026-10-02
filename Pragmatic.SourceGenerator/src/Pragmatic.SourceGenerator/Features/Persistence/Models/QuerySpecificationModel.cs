namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents a query property whose type is a <c>Specification&lt;TEntity&gt;</c>.
/// </summary>
/// <remarks>
///     <para>
///         Recognised by type rather than by an attribute: the type already says what the property is,
///         and a marker would be a second way to say the same thing. Each one generates a
///         <c>query.Where(spec)</c> inside <c>Apply()</c> and a conjunction inside
///         <c>ToSpecification()</c> — the two have to agree, or the same query answers differently
///         through the runner and through the repository.
///     </para>
///     <para>
///         <b>Combined with AND</b>, like every other contribution: each is a separate <c>Where</c>.
///         An alternative belongs <em>inside</em> one property — <c>a | b</c> — where the reader can see
///         it, which is the same rule a <c>[FilterDto]</c> follows with its groups.
///     </para>
///     <para>
///         Null is skipped, so a property that decides at runtime whether its rule applies simply
///         answers <see langword="null" /> when it does not.
///     </para>
/// </remarks>
internal sealed record QuerySpecificationModel
{
    /// <summary>The property name on the query class (e.g. "Confirmed").</summary>
    public required string PropertyName { get; init; }

    /// <summary>Whether the property type is nullable, and so has to be guarded before use.</summary>
    public required bool IsNullable { get; init; }

    /// <summary>
    ///     Whether the property is static: a rule that reads no input, which the analyzers ask to be
    ///     written static (CA1822). Read through the type rather than <c>this</c>.
    /// </summary>
    public bool IsStatic { get; init; }

    /// <summary>How the generated query reads the property.</summary>
    public string AccessFrom(string queryTypeName) => IsStatic ? $"{queryTypeName}.{PropertyName}" : $"this.{PropertyName}";
}
