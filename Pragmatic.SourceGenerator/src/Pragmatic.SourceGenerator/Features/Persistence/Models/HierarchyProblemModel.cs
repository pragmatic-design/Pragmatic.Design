namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A <c>[GenerateHierarchy]</c> that did not resolve, carried in the model so the feature can
///     report it.
/// </summary>
/// <remarks>
///     Carried rather than answered as a dropped model: a transform that returns <c>null</c> here is
///     indistinguishable from "this node is not mine", the pipeline filters it out, and the attribute
///     generates nothing in silence.
/// </remarks>
/// <param name="Kind">What went wrong.</param>
/// <param name="Detail">The name the message points at: the type, the <c>Via</c>, or the edge.</param>
/// <param name="Candidates">The self-referencing navigations that were available, for the message.</param>
internal sealed record HierarchyProblemModel(
    HierarchyProblemKind Kind,
    string Detail,
    string Candidates = "");
