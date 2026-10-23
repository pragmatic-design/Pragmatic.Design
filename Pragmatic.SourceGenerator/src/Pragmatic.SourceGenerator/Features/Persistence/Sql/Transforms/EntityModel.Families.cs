using System.Collections.Generic;
using System.Linq;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The filter families of a Pragmatic entity, as Pragmatic declares them: the tenant (<c>ITenantEntity</c>), the owner (<c>[HasOwner]</c>),
///     the scopes (<c>[HasAccessScopes]</c>), both together (owner OR scope), the soft delete. Each predicate is marked with its slot
///     (<c>{fam:key}</c>…<c>{/fam:key}</c>): the plan numbers the slots it reads, and the runtime keeps or replaces each predicate by the
///     caller's lifts (<c>SqlPlanFilters</c>). The values are tokens the plan makes its last parameters: <c>{ft}</c> the tenant, <c>{fu}</c>
///     the user, <c>{fs}</c> the user's scopes; <c>{scopes:column}</c> is the overlap of a row's scopes with them, per dialect.
/// </summary>
internal sealed partial class EntityModel
{
    /// <summary>True for a Pragmatic <c>[Entity]</c>: its filters are the families, decided per execution.</summary>
    public bool IsPragmatic => PragmaticShape.IsPragmaticEntity(Type);

    /// <summary>The tenant and permission families on <c>t</c>, each marked; null when the entity has neither (or is not Pragmatic's).</summary>
    public string? FamilyPredicate
    {
        get
        {
            if (!IsPragmatic)
                return null;
            var parts = new List<string>();
            if (Type.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.MultiTenancy.ITenantEntity") && Column("TenantId") is { } tenant)
                parts.Add(Mark("Tenant", "TenantFilter", "Tenant", null, $"t.\"{tenant.Column}\" = {{ft}}"));
            var owner = PragmaticShape.Has(Type, "HasOwnerAttribute") ? Column("OwnerId") : null;
            var scopes = PragmaticShape.Has(Type, "HasAccessScopesAttribute") ? Column("AccessScopes") : null;
            var bypass = PragmaticPermissions.ViewAll(Type);
            if (owner is not null && scopes is not null)
                parts.Add(Mark("Permission", "DataAccessFilter", null, bypass, $"t.\"{owner.Column}\" = {{fu}} OR {{scopes:t.\"{scopes.Column}\"}}{ComputedScopesOr}"));
            else if (owner is not null)
                parts.Add(Mark("Permission", "OwnershipFilter", null, bypass, $"t.\"{owner.Column}\" = {{fu}}"));
            else if (scopes is not null)
                parts.Add(Mark("Permission", "ScopedDataFilter", null, bypass, $"{{scopes:t.\"{scopes.Column}\"}}{ComputedScopesOr}"));
            if (ParentVisibility is { } parent)
                parts.Add(Mark("Permission", "ParentVisibilityFilter", null, parent.Bypass, parent.Predicate)); // a trait's child: where its parent is visible
            // [TemporalRelation]: active at the caller's instant ({fn}), as TemporalFilter's ValidFrom <= now && (ValidTo == null || ValidTo > now).
            if (PragmaticFilters.IsTemporal(Type) && Column("ValidFrom") is { } from && Column("ValidTo") is { } to)
                parts.Add(Mark("Temporal", "TemporalFilter", null, null, $"t.\"{from.Column}\" <= {{fn}} AND (t.\"{to.Column}\" IS NULL OR t.\"{to.Column}\" > {{fn}})"));
            return parts.Count == 0 ? null : string.Join(" AND ", parts);
        }
    }

    /// <summary>A rule's predicate on a Pragmatic entity, marked as its own slot: lifted only by its name (<c>Visibility:{rule}</c>), as on EF.</summary>
    public string MarkRule(Microsoft.CodeAnalysis.INamedTypeSymbol rule, string predicate)
        => Mark("Rule", null, "Visibility:" + rule.ToDisplayString(), null, predicate);

    /// <summary>
    ///     The predicate between its slot's marks. The key carries what the plan declares for the slot: family, entity, filter class, the name a
    ///     named lift uses, the bypass permission (<c>Permission|Probe.Binding.Reservation|Probe.Binding.Reservation.OwnershipFilter||reservation.view-all</c>).
    /// </summary>
    private string Mark(string family, string? filter, string? name, string? bypass, string predicate)
        => MarkAs(family, filter is null ? "" : Type.ToDisplayString() + "." + filter, name, bypass, predicate);

    /// <summary>A mark whose filter is any type (<paramref name="filterType" />, its full name): a data scope rule's slot names its rule.</summary>
    private string MarkAs(string family, string filterType, string? name, string? bypass, string predicate)
    {
        var key = $"{family}|{Type.ToDisplayString()}|{filterType}|{name}|{bypass}";
        return $"{{fam:{key}}}({predicate}){{/fam:{key}}}";
    }

    /// <summary>
    ///     The data scope rules of a scoped entity computed at read time (<c>ComputedScopeFilter</c>, priority 260): each rule's predicate in the
    ///     scopes' OR, its own slot (active when the caller holds its scope and the rule is registered Computed or Hybrid; otherwise false).
    /// </summary>
    public List<(string Rule, string Predicate)> ComputedScopes { get; } = [];

    private string ComputedScopesOr => string.Concat(ComputedScopes.Select(r => " OR " + MarkAs("ComputedScope", r.Rule, null, null, r.Predicate)));

    /// <summary>
    ///     Every <c>DataScopeRule&lt;T&gt;</c> of the compilation on this entity, its <c>ToExpression()</c> lambda translated on <c>t</c> with its
    ///     constants as literals (a fixed predicate the family text carries in every position). A rule that does not translate is reported
    ///     (PRAG3035) and left out, so the read refuses it when it is registered (<c>CompiledScopeRules</c>).
    /// </summary>
    internal void ReadComputedScopes(Microsoft.CodeAnalysis.Compilation compilation, List<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
    {
        if (!IsPragmatic || !PragmaticShape.Has(Type, "HasAccessScopesAttribute"))
            return;
        foreach (var rule in PragmaticScopeRules.Of(compilation, Type))
        {
            if (!PragmaticScopeRules.IsRead(rule))
                continue; // the saves' only (ScopeMaterializer): the reads match the scope it stored
            var where = rule.Locations.FirstOrDefault() ?? Microsoft.CodeAnalysis.Location.None;
            var lambda = SqlInterceptorGenerator.RuleLambda(rule, compilation, out var model);
            if (lambda is null || model is null || !SqlInterceptorGenerator.TryParameter(model, lambda, out var parameter))
            {
                diagnostics.Add(Microsoft.CodeAnalysis.Diagnostic.Create(Diagnostics.ScopeRuleNotCompiled, where, rule.Name, "its ToExpression must be an expression-bodied lambda in this compilation"));
                continue;
            }

            var translator = new LambdaTranslator(model, t => Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(t, Type) ? this : null, new EntityRow(this, "t"), parameter) { InlineConstants = true };
            var predicate = translator.Predicate(SqlInterceptorGenerator.Body(lambda));
            var pieces = translator.DialectPieces;
            if (predicate is null || translator.ParameterPaths.Count > 0 || translator.Joins.Count > 0 || pieces.Any(p => !(p.Sqlite == "1" && p.PostgreSql == "true" && p.SqlServer == "1")))
            {
                diagnostics.Add(Microsoft.CodeAnalysis.Diagnostic.Create(Diagnostics.ScopeRuleNotCompiled, where, rule.Name,
                    predicate is null ? $"'{translator.ErrorExpression}' {translator.Error}" : "a rule is a fixed predicate on the row's own columns (no value of the moment, no navigation)"));
                continue;
            }

            for (var i = 0; i < pieces.Count; i++)
                predicate = predicate.Replace("{d" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", "{bt:1}"); // the boolean true each dialect writes its way
            ComputedScopes.Add((rule.ToDisplayString(), predicate));
        }
    }

    /// <summary>
    ///     A trait's child (<c>[TraitOf&lt;TParent&gt;]</c>): its parent's owner and scopes, restated over the parent's row (<c>ParentVisibilityFilter</c>:
    ///     the parent's predicate through the navigation, not the parent's filters), as an EXISTS on the parent's table by the child's foreign key;
    ///     its bypass the parent's. Null for any other entity.
    /// </summary>
    public (string Predicate, string Bypass)? ParentVisibility { get; private set; }

    internal void ReadParentVisibility(IReadOnlyList<Microsoft.CodeAnalysis.INamedTypeSymbol>? entities, Microsoft.CodeAnalysis.Compilation compilation)
    {
        if (!IsPragmatic || PragmaticFilters.TraitParent(Type) is not { } parent)
            return;
        var navigation = Navigations.FirstOrDefault(n => Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(n.Target, parent));
        if (navigation is null || Column(navigation.ForeignKey) is not { } foreignKey || Read(parent, [], entities, compilation) is not { Keys.Count: 1 } target)
            return;
        var owner = PragmaticShape.Has(parent, "HasOwnerAttribute") ? target.Column("OwnerId") : null;
        var scopes = PragmaticShape.Has(parent, "HasAccessScopesAttribute") ? target.Column("AccessScopes") : null;
        var condition = (owner, scopes) switch
        {
            ({ } o, { } s) => $"tp.\"{o.Column}\" = {{fu}} OR {{scopes:tp.\"{s.Column}\"}}",
            ({ } o, null) => $"tp.\"{o.Column}\" = {{fu}}",
            (null, { } s) => $"{{scopes:tp.\"{s.Column}\"}}",
            _ => null,
        };
        if (condition is not null)
            ParentVisibility = ($"EXISTS (SELECT 1 FROM \"{target.Table}\" tp WHERE tp.\"{target.Keys[0].Column}\" = t.\"{foreignKey.Column}\" AND ({condition}))", PragmaticPermissions.ViewAll(parent));
    }

    /// <summary>The soft delete of a Pragmatic entity, marked: lifted by Raw, or by both its halves on EF (the provider's and the named one).</summary>
    private string? MarkedSoftDelete(string? predicate)
        => predicate is null || !IsPragmatic ? predicate : Mark("SoftDelete", "SoftDeleteFilter", "SoftDelete", null, predicate);
}
