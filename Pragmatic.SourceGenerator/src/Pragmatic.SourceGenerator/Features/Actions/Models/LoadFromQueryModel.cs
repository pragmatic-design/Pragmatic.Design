using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     A property of an operation filled by a declared query — <c>[LoadFrom&lt;TQuery&gt;]</c> — before the body
///     runs.
/// </summary>
internal sealed record LoadFromQueryModel
{
    /// <summary>The operation's property the result is written to.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The property's type, fully qualified — to be the query's answer.</summary>
    public required string PropertyTypeFullName { get; init; }

    /// <summary>The query, fully qualified.</summary>
    public required string QueryTypeFullName { get; init; }

    /// <summary>The query's inputs, each bound by name to a property of the operation.</summary>
    public EquatableArray<QueryInputBindingModel> Inputs { get; init; } = EquatableArray<QueryInputBindingModel>.Empty;

    /// <summary>
    ///     Why the property cannot be filled by the query — it is no declared query of this compilation, or the
    ///     property is not of its answer — found against the query models; null when it can (<c>PRAG0458</c>).
    /// </summary>
    public string? ResultProblem { get; init; }

    /// <summary>Whether the query models have been consulted yet.</summary>
    public bool IsResolved { get; init; }

    /// <summary>The local the result is held in until it is written.</summary>
    public string Variable => "__" + char.ToLowerInvariant(PropertyName[0]) + PropertyName.Substring(1);
}
