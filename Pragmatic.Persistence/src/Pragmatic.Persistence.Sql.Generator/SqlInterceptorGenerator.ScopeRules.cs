using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The data scope rules (<c>DataScopeRule&lt;T&gt;</c>) of this assembly's scoped Pragmatic entities, registered at module start with the Sql
///     engine's bridge (<c>CompiledScopeRules</c>): each rule's predicate for the saves (its own tree, interpreted), and whether the reads'
///     statements carry it (<see cref="EntityModel.ComputedScopes" />).
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    private const string CompiledScopeRules = "Pragmatic.Persistence.Sql.Pragmatic.CompiledScopeRules";

    private static void EmitScopeRules(StringBuilder source, List<EntityModel> entities, Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(CompiledScopeRules) is null)
            return;
        var registrations = new List<string>();
        foreach (var entity in entities.Where(e => e.IsPragmatic && SymbolEqualityComparer.Default.Equals(e.Type.ContainingAssembly, compilation.Assembly)))
        {
            var type = entity.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            foreach (var rule in PragmaticScopeRules.Of(compilation, entity.Type))
            {
                var readable = entity.ComputedScopes.Any(c => c.Rule == rule.ToDisplayString()) ? "true" : "false";
                registrations.Add($"global::{CompiledScopeRules}.Register(typeof({rule.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), static rule => {{ var matches = ((global::Pragmatic.Persistence.Scopes.DataScopeRule<{type}>)rule).ToExpression().Compile(preferInterpretation: true); return row => matches(({type})row); }}, readable: {readable});");
            }
        }

        if (registrations.Count == 0)
            return;
        source.AppendLine("    file static class ScopeRuleRegistrations");
        source.AppendLine("    {");
        source.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("        internal static void Register()");
        source.AppendLine("        {");
        foreach (var registration in registrations)
            source.AppendLine("            " + registration);
        source.AppendLine("        }");
        source.AppendLine("    }");
    }
}
