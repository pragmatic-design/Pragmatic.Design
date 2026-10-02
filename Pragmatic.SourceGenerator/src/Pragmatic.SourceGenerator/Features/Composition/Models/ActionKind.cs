// Pragmatic.SourceGenerator - Composition - Action Kind Enum

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Classifies an action type for endpoint handler generation.
/// </summary>
internal enum ActionKind
{
    /// <summary>Unknown or unsupported action base type.</summary>
    Unknown = 0,

    /// <summary>DomainAction&lt;TReturn&gt; — returns Result&lt;TReturn, IError&gt;.</summary>
    DomainAction = 1,

    /// <summary>VoidDomainAction — returns VoidResult.</summary>
    VoidDomainAction = 2,

    /// <summary>Mutation&lt;TEntity&gt; — returns Result&lt;TEntity, IError&gt;.</summary>
    Mutation = 3
}
