using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The computed scopes of a scoped Pragmatic entity on documents (<c>ComputedScopeFilter</c>): each <c>DataScopeRule&lt;T&gt;</c> the reads carry
///     (Computed or Hybrid), its condition in the scopes' OR, active per call when the caller holds its scope (the slot's state, asked of the
///     caller's filters). MongoDB writes the condition only then; Cosmos DB's text carries it behind a boolean parameter (<c>@__csN</c>). A rule
///     that does not translate is reported (PRAG3110) and left out, so the read refuses it when it is registered (<c>CompiledScopeRules</c>).
/// </summary>
internal static partial class DocFamiliesEmitter
{
    private static List<(string Rule, string Slot, string Mongo, string Cosmos, string? Parameters, int Index)> ComputedScopes(
        DocModel model, Compilation compilation, List<Diagnostic> diagnostics, string type)
    {
        var computed = new List<(string, string, string, string, string?, int)>();
        foreach (var rule in PragmaticScopeRules.Of(compilation, model.Type).Where(PragmaticScopeRules.IsRead))
        {
            var index = computed.Count;
            var reported = new List<Diagnostic>();
            if (Rule(model, rule, compilation, reported, $"@__cs{index}_") is not { } condition)
            {
                var why = reported.FirstOrDefault()?.GetMessage() ?? "its ToExpression must be an expression-bodied lambda in this compilation";
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.ScopeRuleNotCompiled, rule.Locations.FirstOrDefault() ?? Location.None, rule.Name, why));
                continue;
            }

            var ruleSlot = $"new {Runtime}.DocFilterSlot(typeof({type}), {Runtime}.DocFilterFamily.ComputedScope, typeof({rule.ToDisplayString(DocModel.Qualified)}), null)";
            computed.Add((rule.ToDisplayString(), ruleSlot, condition.Mongo, condition.Cosmos, condition.Parameters, index));
        }

        return computed;
    }

    /// <summary>
    ///     The registration of a Pragmatic entity's data scope rules with the documents engine's bridge (<c>CompiledScopeRules.Register</c>): each
    ///     rule's predicate for the saves, and whether the reads carry it (a scoped entity's Computed or Hybrid rule that translated).
    /// </summary>
    public static IEnumerable<string> ScopeRuleRegistrations(DocModel model, Compilation compilation)
    {
        var readable = PragmaticShape.Has(model.Type, "HasAccessScopesAttribute") ? ComputedScopes(model, compilation, [], model.Type.ToDisplayString(DocModel.Qualified)).Select(c => c.Rule).ToList() : [];
        var type = model.Type.ToDisplayString(DocModel.Qualified);
        foreach (var rule in PragmaticScopeRules.Of(compilation, model.Type))
        {
            yield return $"global::Pragmatic.Persistence.Documents.Pragmatic.CompiledScopeRules.Register(typeof({rule.ToDisplayString(DocModel.Qualified)}), static rule => {{ var matches = ((global::Pragmatic.Persistence.Scopes.DataScopeRule<{type}>)rule).ToExpression().Compile(preferInterpretation: true); return row => matches(({type})row); }}, readable: {(readable.Contains(rule.ToDisplayString()) ? "true" : "false")});";
        }
    }

    /// <summary>
    ///     A trait's child (<c>[TraitOf&lt;TParent&gt;]</c>) of an owned or scoped parent: its foreign key among the keys of the parents the caller may
    ///     see by the parent's own owner and scopes (<c>ParentVisibilityFilter</c>), read before each execution (<c>DocFamilies.PrepareAsync</c>); its
    ///     bypass the parent's. Null for any other entity.
    /// </summary>
    private static string? ParentVisibility(DocModel model, Compilation compilation, string type)
    {
        if (PragmaticFilters.TraitParent(model.Type) is not { } parent || compilation.GetTypeByMetadataName(parent.ToDisplayString()) is not { } shaped
            || DocModel.Read(shaped, []) is not { } parentModel || model.Field(parent.Name + "Id") is not { } foreignKey)
            return null;
        var owner = PragmaticShape.Has(parent, "HasOwnerAttribute") ? parentModel.Field("OwnerId")?.Name : null;
        var scopes = PragmaticShape.Has(parent, "HasAccessScopesAttribute") ? parentModel.Field("AccessScopes")?.Name : null;
        if (owner is null && scopes is null)
            return null;
        var slot = $"new {Runtime}.DocFilterSlot(typeof({type}), {Runtime}.DocFilterFamily.Permission, typeof({type}.ParentVisibilityFilter), null, {DocTypeEmitter.Literal(PragmaticPermissions.ViewAll(parent))})";
        var mongo = $"w.StartDocument({DocTypeEmitter.Literal(foreignKey.Name)}); {Runtime}.DocValues.WriteArray(w, \"$in\", {Runtime}.DocFamilies.VisibleParents<{type}>(), {Runtime}.DocValues.Write); w.EndDocument();";
        var cosmos = $"ARRAY_CONTAINS(@__parents, {DocCosmosText.Path(foreignKey.Name, "c")})";
        var parameter = $"w.WriteStartObject(); w.WriteString(\"name\", \"@__parents\"); w.WriteStartArray(\"value\"); foreach (var id in {Runtime}.DocFamilies.VisibleParents<{type}>()) w.WriteStringValue(id.ToString(\"D\")); w.WriteEndArray(); w.WriteEndObject();";
        var read = $"static (s, f, ct) => {Runtime}.DocFamilies.ParentKeysAsync<{parent.ToDisplayString(DocModel.Qualified)}>(s, f, {(owner is null ? "null" : DocTypeEmitter.Literal(owner))}, {(scopes is null ? "null" : DocTypeEmitter.Literal(scopes))}, ct)";
        return $"new {Runtime}.DocFamily({slot}, static (w, f) => {{ {mongo} }}, {DocTypeEmitter.Literal(cosmos)}, static (w, f) => {{ {parameter} }}, false, {read})";
    }

    /// <summary>Each computed scope after the scopes' own condition: in MongoDB's <c>$or</c> when active, in Cosmos DB's behind its boolean.</summary>
    private static void AppendComputed(List<(string Rule, string Slot, string Mongo, string Cosmos, string? Parameters, int Index)> computed, StringBuilder mongo, StringBuilder cosmos, StringBuilder parameters)
    {
        foreach (var rule in computed)
        {
            var active = $"f is {{ }} c{rule.Index} && c{rule.Index}.StateOf({rule.Slot}) == {Runtime}.DocFilterState.Active";
            mongo.Append($"if ({active}) {{ w.StartDocument(i++); {rule.Mongo} w.EndDocument(); }} ");
            cosmos.Append($" OR (@__cs{rule.Index} = true AND ({rule.Cosmos}))");
            parameters.Append($"w.WriteStartObject(); w.WriteString(\"name\", \"@__cs{rule.Index}\"); w.WriteBoolean(\"value\", {active}); w.WriteEndObject(); ");
            if (rule.Parameters is { } own)
                parameters.Append(own).Append(' ');
        }
    }
}
