using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Templates;

/// <summary>
///     <c>Pragmatic.Persistence.Sql.g.cs</c>: the chains translated at compile time. The interceptors, the plans they run, the maps that read
///     a row and the compiled predicates live in one namespace as file-local types, so that only the interceptors reach them; the hierarchy
///     walks, the views and the schemas follow, in the entities' namespaces.
/// </summary>
internal sealed partial class SqlSourceTemplate(EntityMapsModel maps, EquatableArray<ScopeRuleModel> scopeRules, EquatableArray<PlanSectionModel> plans, EquatableArray<CompiledPredicateModel> predicates, EquatableArray<InterceptorModel> interceptors, string trailer) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Persistence.Sql";

    protected override string? SourceInfo => "LINQ translated at compile time";

    public override Artifact RenderOutput() => new("Pragmatic.Persistence.Sql.g.cs", ToSourceText());

    public override void RenderFile()
    {
        // The compiler matches the attribute by name; file-local, it collides with no other generator's copy.
        AppendLine("namespace System.Runtime.CompilerServices");
        AppendLine("{");
        AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
        AppendLine("    {");
        AppendLine("        public InterceptsLocationAttribute(int version, string data) { }");
        AppendLine("    }");
        AppendLine("}");
        AppendLine();
        AppendLine("namespace Pragmatic.Persistence.Sql.Generated");
        AppendLine("{");
        AppendLine("    using global::System;");
        AppendLine("    using global::System.Linq.Expressions;");
        AppendLine("    using global::Pragmatic.Persistence.Sql;");
        AppendLine();
        RenderMaps(maps);
        RenderScopeRules(scopeRules);
        RenderPlans(plans);
        RenderCompiledPredicates(predicates);
        RenderInterceptors(interceptors);
        AppendLine("}");
        Text(trailer);
    }

    /// <summary>A section already written, line by line.</summary>
    /// <remarks>TODO: the sections become models one at a time (the engines' port, step 4); this goes with the last of them.</remarks>
    private void Text(string text)
    {
        foreach (var line in TextLines.Of(text))
            AppendLine(line);
    }
}
