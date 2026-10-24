using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The data scope rules (<c>DataScopeRule&lt;T&gt;</c>) of this assembly's scoped Pragmatic entities, registered at module start with the Sql
///     engine's bridge (<c>CompiledScopeRules</c>): each rule's predicate for the saves (its own tree, interpreted), and whether the reads'
///     statements carry it (<see cref="EntityModel.ComputedScopes" />).
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    private const string CompiledScopeRules = "Pragmatic.Persistence.Sql.CompiledScopeRules";

    /// <summary>The rules to register; none without the bridge.</summary>
    private static EquatableArray<ScopeRuleModel> ReadScopeRules(List<EntityModel> entities, Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(CompiledScopeRules) is null)
            return EquatableArray<ScopeRuleModel>.Empty;
        return entities.Where(e => e.IsPragmatic && SymbolEqualityComparer.Default.Equals(e.Type.ContainingAssembly, compilation.Assembly))
            .SelectMany(entity => PragmaticScopeRules.Of(compilation, entity.Type).Select(rule => new ScopeRuleModel(
                rule.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                entity.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                entity.ComputedScopes.Any(c => c.Rule == rule.ToDisplayString()))))
            .ToEquatableArray();
    }
}
