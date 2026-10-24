using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The filter families of a Pragmatic entity as documents filter them, Pragmatic's as the Sql engine declares them (<c>EntityModel.Families</c>):
///     the soft delete, the tenant (<c>ITenantEntity</c>), the owner, the scopes, both together (owner OR scope), and each <c>[VisibleWhen]</c> rule
///     translated like a chain's lambda (a rule reading a value of the moment is refused: it would not be fixed). Each is a <c>DocFamily</c>: its
///     slot, its MongoDB condition written from the caller, its Cosmos DB condition and parameters (<c>@__tenant</c>, <c>@__user</c>,
///     <c>@__scopes</c>, a rule's <c>@__rN_</c>).
/// </summary>
internal static partial class DocFamiliesEmitter
{
    private const string Runtime = "global::Pragmatic.Persistence.Documents";
    private const string Nothing = "w.StartDocument(\"_id\"); w.Write(\"$exists\", false); w.EndDocument();"; // no document lacks its _id: none matches

    /// <summary>The <c>new DocFamily(…)</c> initializers of the entity, in Pragmatic's order; empty when it has none.</summary>
    public static List<string> Families(DocModel model, Compilation compilation, List<Diagnostic> diagnostics)
    {
        var families = new List<string>();
        if (!model.IsPragmaticEntity)
            return families;
        var entity = model.Type;
        var type = entity.ToDisplayString(DocModel.Qualified);
        string Slot(string family, string? filter, string? name, string? bypass = null)
            => $"new {Runtime}.DocFilterSlot(typeof({type}), {Runtime}.DocFilterFamily.{family}, {(filter is null ? "null" : $"typeof({type}.{filter})")}, {(name is null ? "null" : DocTypeEmitter.Literal(name))}{(bypass is null ? "" : ", " + DocTypeEmitter.Literal(bypass))})";
        string Field(string property) => model.Field(property)?.Name ?? property;
        string Path(string property) => DocCosmosText.Path(Field(property), "c");

        if (PragmaticShape.Has(entity, "SoftDeleteAttribute") && model.Field("IsDeleted") is not null)
            families.Add($"new {Runtime}.DocFamily({Slot("SoftDelete", "SoftDeleteFilter", "SoftDelete")}, static (w, f) => w.Write({DocTypeEmitter.Literal(Field("IsDeleted"))}, false), {DocTypeEmitter.Literal(Path("IsDeleted") + " = false")}, null)");

        if (entity.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.MultiTenancy.ITenantEntity") && model.Field("TenantId") is not null)
        {
            var partitions = model.PartitionKey?.Property.Name == "TenantId" ? "true" : "false";
            families.Add($"new {Runtime}.DocFamily({Slot("Tenant", "TenantFilter", "Tenant")}, static (w, f) => {{ if (f?.TenantId is {{ }} tenant) {Runtime}.DocValues.Write(w, {DocTypeEmitter.Literal(Field("TenantId"))}, tenant); else {{ {Nothing} }} }}, "
                         + $"{DocTypeEmitter.Literal(Path("TenantId") + " = @__tenant")}, static (w, f) => {Text("@__tenant", "f?.TenantId")}, {partitions})");
        }

        var owner = PragmaticShape.Has(entity, "HasOwnerAttribute") && model.Field("OwnerId") is not null;
        var scoped = PragmaticShape.Has(entity, "HasAccessScopesAttribute") && model.Field("AccessScopes") is not null;
        var bypass = PragmaticPermissions.ViewAll(entity);
        var ownerMongo = $"if (f?.UserId is {{ }} user) {Runtime}.DocValues.Write(w, {DocTypeEmitter.Literal(Field("OwnerId"))}, user); else {{ {Nothing} }}";
        var scopesMongo = $"w.StartDocument({DocTypeEmitter.Literal(Field("AccessScopes"))}); {Runtime}.DocValues.WriteArray(w, \"$in\", f?.Scopes ?? global::System.Array.Empty<string>(), {Runtime}.DocValues.Write); w.EndDocument();";
        var ownerCosmos = Path("OwnerId") + " = @__user";
        var scopesCosmos = $"EXISTS(SELECT VALUE s FROM s IN {Path("AccessScopes")} WHERE ARRAY_CONTAINS(@__scopes, s))";
        var scopesParameter = "w.WriteStartObject(); w.WriteString(\"name\", \"@__scopes\"); w.WriteStartArray(\"value\"); foreach (var scope in f?.Scopes ?? global::System.Array.Empty<string>()) w.WriteStringValue(scope); w.WriteEndArray(); w.WriteEndObject();";
        var computed = scoped ? ComputedScopes(model, compilation, diagnostics, type) : [];
        if (owner && scoped)
        {
            var mongo = new StringBuilder($"w.StartArray(\"$or\"); var i = 0; w.StartDocument(i++); {ownerMongo} w.EndDocument(); w.StartDocument(i++); {scopesMongo} w.EndDocument(); ");
            var cosmos = new StringBuilder($"({ownerCosmos} OR {scopesCosmos}");
            var parameters = new StringBuilder($"{Text("@__user", "f?.UserId")}; {scopesParameter} ");
            AppendComputed(computed, mongo, cosmos, parameters);
            families.Add($"new {Runtime}.DocFamily({Slot("Permission", "DataAccessFilter", null, bypass)}, static (w, f) => {{ {mongo}w.EndArray(); }}, {DocTypeEmitter.Literal(cosmos + ")")}, static (w, f) => {{ {parameters}}})");
        }
        else if (owner)
            families.Add($"new {Runtime}.DocFamily({Slot("Permission", "OwnershipFilter", null, bypass)}, static (w, f) => {{ {ownerMongo} }}, {DocTypeEmitter.Literal(ownerCosmos)}, static (w, f) => {Text("@__user", "f?.UserId")})");
        else if (scoped && computed.Count == 0)
            families.Add($"new {Runtime}.DocFamily({Slot("Permission", "ScopedDataFilter", null, bypass)}, static (w, f) => {{ {scopesMongo} }}, {DocTypeEmitter.Literal(scopesCosmos)}, static (w, f) => {{ {scopesParameter} }})");
        else if (scoped)
        {
            var mongo = new StringBuilder($"w.StartArray(\"$or\"); var i = 0; w.StartDocument(i++); {scopesMongo} w.EndDocument(); ");
            var cosmos = new StringBuilder($"({scopesCosmos}");
            var parameters = new StringBuilder($"{scopesParameter} ");
            AppendComputed(computed, mongo, cosmos, parameters);
            families.Add($"new {Runtime}.DocFamily({Slot("Permission", "ScopedDataFilter", null, bypass)}, static (w, f) => {{ {mongo}w.EndArray(); }}, {DocTypeEmitter.Literal(cosmos + ")")}, static (w, f) => {{ {parameters}}})");
        }

        if (ParentVisibility(model, compilation, type) is { } parent)
            families.Add(parent);

        // [TemporalRelation]: the stretches active at the caller's instant (TemporalFilter's ValidFrom <= now && (ValidTo == null || ValidTo > now)).
        if (PragmaticFilters.IsTemporal(entity) && model.Field("ValidFrom") is not null && model.Field("ValidTo") is not null)
        {
            var (from, to) = (DocTypeEmitter.Literal(Field("ValidFrom")), DocTypeEmitter.Literal(Field("ValidTo")));
            var mongo = $"if (f is {{ }} c) {{ var now = c.Now; w.StartDocument({from}); {Runtime}.DocValues.Write(w, \"$lte\", now); w.EndDocument(); "
                        + $"w.StartArray(\"$or\"); w.StartDocument(0); w.WriteNull({to}); w.EndDocument(); w.StartDocument(1); w.StartDocument({to}); {Runtime}.DocValues.Write(w, \"$gt\", now); w.EndDocument(); w.EndDocument(); w.EndArray(); }} else {{ {Nothing} }}";
            var cosmos = $"({Path("ValidFrom")} <= @__now AND (NOT IS_DEFINED({Path("ValidTo")}) OR IS_NULL({Path("ValidTo")}) OR {Path("ValidTo")} > @__now))";
            var parameter = $"w.WriteStartObject(); w.WriteString(\"name\", \"@__now\"); if (f is {{ }} c) w.WriteString(\"value\", {Runtime}.DocJson.Instant(c.Now)); else w.WriteNull(\"value\"); w.WriteEndObject();";
            families.Add($"new {Runtime}.DocFamily({Slot("Temporal", "TemporalFilter", null)}, static (w, f) => {{ {mongo} }}, {DocTypeEmitter.Literal(cosmos)}, static (w, f) => {{ {parameter} }})");
        }

        var index = 0;
        foreach (var rule in Rules(entity))
        {
            if (Rule(model, rule, compilation, diagnostics, $"@__r{index++}_") is not { } condition)
                continue;
            var (mongo, cosmos, parameters) = condition;
            families.Add($"new {Runtime}.DocFamily({Slot("Rule", null, "Visibility:" + rule.ToDisplayString())}, static (w, f) => {{ {mongo} }}, {DocTypeEmitter.Literal(cosmos)}, {(parameters is null ? "null" : $"static (w, f) => {{ {parameters} }}")})");
        }

        return families;
    }

    /// <summary>One Cosmos DB parameter whose value is a string or null.</summary>
    private static string Text(string name, string value)
        => $"{{ w.WriteStartObject(); w.WriteString(\"name\", \"{name}\"); if ({value} is {{ }} v) w.WriteString(\"value\", v); else w.WriteNull(\"value\"); w.WriteEndObject(); }}";

    /// <summary>The entity's rules and its base types' (<c>[VisibleWhen&lt;Rule&gt;]</c>), as the Sql engine collects them.</summary>
    private static IEnumerable<INamedTypeSymbol> Rules(INamedTypeSymbol entity)
    {
        for (var current = entity; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass is { Name: "VisibleWhenAttribute", TypeArguments.Length: 1 } mark && mark.TypeArguments[0] is INamedTypeSymbol rule)
                    yield return rule;
            }
        }
    }

    /// <summary>A rule's <c>ToExpression()</c> lambda as the entity's condition (MongoDB statements, Cosmos DB text and parameters); null, reported, when it is not translated.</summary>
    private static (string Mongo, string Cosmos, string? Parameters)? Rule(DocModel model, INamedTypeSymbol rule, Compilation compilation, List<Diagnostic> diagnostics, string prefix)
    {
        var where = rule.Locations.FirstOrDefault() ?? Location.None;
        var lambda = rule.GetMembers("ToExpression").OfType<IMethodSymbol>().SelectMany(m => m.DeclaringSyntaxReferences).Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>()
            .Select(m => m.ExpressionBody?.Expression ?? m.Body?.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault()?.Expression).OfType<SimpleLambdaExpressionSyntax>().FirstOrDefault();
        if (lambda?.Body is not ExpressionSyntax body)
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, where, rule.Name + ".ToExpression()", "a rule on a document is a lambda in its source, returned by ToExpression (=> e => …)"));
            return null;
        }

        var semantic = compilation.GetSemanticModel(lambda.SyntaxTree);
        if (semantic.GetDeclaredSymbol(lambda.Parameter) is not IParameterSymbol parameter)
            return null;
        var translator = new DocTranslator(semantic, model, t => DocModel.Read(t, new List<Diagnostic>()), parameter, null, null, diagnostics, where);
        var condition = translator.Condition(body);
        if (condition is null || translator.Failed)
            return null;
        var mongo = new StringBuilder();
        condition.Render(mongo, "");
        var context = new DocCosmosText(prefix);
        var cosmos = condition.RenderCosmos(context, "c");
        return (mongo.ToString().Replace("\r", "").Replace("\n", " "), cosmos, context.HasParameters ? context.Parameters : null);
    }
}
