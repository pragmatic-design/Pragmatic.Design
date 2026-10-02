using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Templates;

/// <summary>
///     Projection and Selector generation: EF Core Expression trees and in-memory Func delegates.
/// </summary>
internal sealed partial class MappingTemplate
{
    /// <summary>
    ///     <c>new </c> when this DTO inherits one this generator also writes statics onto, empty otherwise.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Both halves matter. Without it, <c>CS0108</c> — an <b>error</b> under this repository's
    ///     <c>--warnaserror</c> build — on every <c>[MapFrom]</c> DTO inheriting another, which is the
    ///     shape <c>[MapDerived]</c> requires and <c>PRAG0330</c> enforces. With it on a DTO that hides
    ///     nothing, <c>CS0109</c> — also an error here — on the 160-odd DTOs that have no base.
    /// </remarks>
    private string Hiding => _model.InheritsAMappedDto ? "new " : "";

    // ═══════════════════════════════════════════════════════════════════════════
    // Selector Generation (In-Memory Func<>)
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderSelector()
    {
        XmlSummary($"Func delegate for in-memory mapping of {_model.SourceTypeName} to {_model.TypeName}. Use with LINQ Select() for in-memory collections.");

        // When HasCircularReferences, FromEntity has optional parameter, so we need a lambda
        // Otherwise use method group reference for better performance (no closure allocation)
        var selectorExpr = _model.HasCircularReferences
            ? "e => FromEntity(e)"
            : "FromEntity";

        AppendLine(
            $"public static {Hiding}Func<global::{_model.SourceTypeFullName}, {_model.TypeName}> Selector {{ get; }} = {selectorExpr};");
        AppendLine();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Projection Generation (EF Core Expression<Func<>>)
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderProjection()
    {
        XmlSummary($"Expression for projecting {_model.SourceTypeName} to {_model.TypeName} in EF Core queries.");

        AppendLine(
            $"public static {Hiding}Expression<Func<global::{_model.SourceTypeFullName}, {_model.TypeName}>> Projection {{ get; }} =");
        IncreaseIndent();
        AppendLine($"entity => new {_model.TypeName}");
        Block(() =>
        {
            // Include simple SQL-translatable properties, the formatted and converted ones the client
            // computes after the read, nested DTOs with projection mappings, and collection DTOs with
            // element mappings
            foreach (var prop in _model.Properties.Where(p =>
                         !p.IsIgnored &&
                         p.Resolution != MappingResolution.None &&
                         (p.IsSqlTranslatable ||
                          p.IsComputedAfterTheRead ||
                          p is { IsNestedDto: true, NestedProjectionMappings.Length: > 0 } ||
                          p is { IsElementDto: true, ElementProjectionMappings.Length: > 0 })))
            {
                var expr = GenerateProjectionExpression(prop);

                // [MapCondition] gates the projection as it gates FromEntity, when its predicate could
                // be written over the row. Otherwise the property keeps its default here too.
                // The same fallback FromEntity writes (`: default!`): the two paths answer alike.
                if (prop.ConditionProjectionBody is { } condition)
                {
                    var mapped = expr.Contains(" ? ") ? $"({expr})" : expr;
                    expr = $"({condition}) ? {mapped} : default!";
                }

                AppendLine($"{prop.PropertyName} = {expr},");
            }
        });
        AppendLine(";");
        DecreaseIndent();
        AppendLine();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RequiredNavigations (navigation paths inferred from mapping)
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderRequiredNavigations()
    {
        // "Use with IIncludableQuery" was aspirational when it was written — no generated query
        // implemented that interface. A query answering with the entity does now, so the sentence is
        // true and stays as it is: 162 snapshots move for a rewording, and none of them for a fact.
        XmlSummary(
            "Navigation paths required for this DTO's mapping. " +
            "Use with IIncludableQuery to eagerly load related entities.");

        var paths = string.Join(", ", _model.RequiredNavigations.Select(n => $"\"{n}\""));
        AppendLine(
            $"public static {Hiding}IReadOnlyList<string> RequiredNavigations {{ get; }} = [{paths}];");
        AppendLine();
    }

    /// <summary>
    ///     The navigations the write path merges into, which the caller has to load first.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Separate from <c>RequiredNavigations</c> because the questions are different:
    ///         that list is what a read needs to build the DTO, this one is what a write needs before
    ///         merging into it. On a bidirectional DTO whose read and write shapes differ, so do the
    ///         two sets.
    ///     </para>
    ///     <para>
    ///         Emitted on every <c>[MapTo]</c>, empty list included, for the same reason as its read
    ///         twin: the mutation invoker names it without being able to see this model.
    ///     </para>
    /// </remarks>
    private void RenderWrittenNavigations()
    {
        XmlSummary(
            "Navigation paths this DTO writes into. Load them before applying it: a merge decides "
            + "what to keep by looking at what is there, so an unloaded one is written again.");

        var paths = string.Join(", ", _model.WrittenNavigations.Select(n => $"\"{n}\""));
        AppendLine(
            $"public static {Hiding}IReadOnlyList<string> WrittenNavigations {{ get; }} = [{paths}];");
        AppendLine();
    }
}
