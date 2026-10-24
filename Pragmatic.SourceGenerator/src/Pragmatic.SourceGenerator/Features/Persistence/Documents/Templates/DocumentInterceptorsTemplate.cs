using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;

/// <summary>
///     The document chains translated at compile time: every intercepted call, and each terminal's plan, as file-local members, so that only the
///     interceptors reach the plans.
/// </summary>
internal sealed class DocumentInterceptorsTemplate(EquatableArray<DocumentInterceptorModel> interceptors, EquatableArray<DocSpecificationModel> specifications = default) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Persistence.Documents";

    protected override string? SourceInfo => "the document chains translated at compile time";

    public override Artifact RenderOutput() => new(VirtualFolderHints.ForAssembly("Documents", "Interceptors"), ToSourceText());

    protected override bool Validate() => interceptors.Count > 0 || specifications.Count > 0;

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
        AppendLine("namespace Pragmatic.Persistence.Documents.Generated");
        AppendLine("{");
        AppendLine("    file static class DocumentInterceptors");
        AppendLine("    {");
        foreach (var interceptor in interceptors)
            RenderInterceptor(interceptor);
        foreach (var specification in specifications)
            RenderSpecification(specification);
        AppendLine("    }");
        AppendLine("}");
    }

    /// <summary>A compiled specification's condition, and the interceptor of its call.</summary>
    private void RenderSpecification(DocSpecificationModel specification)
    {
        var (index, type) = (specification.Index, specification.EntityType);
        AppendLine($"        private static readonly global::Pragmatic.Persistence.Documents.Planning.DocCompiledPredicate Specification_{index} = new(static (w, s) =>");
        AppendLine("            {");
        foreach (var line in TextLines.Of(specification.Mongo))
            AppendLine(line);
        AppendLine($"            }}, {specification.CosmosLiteral}, {specification.CosmosWrites}, {specification.CosmosWriter});");
        AppendLine();
        AppendLine($"        {specification.Attribute}");
        AppendLine($"        internal static global::Pragmatic.Specification.Specification<{type}> SpecWhere_{index}(global::System.Linq.Expressions.Expression<global::System.Func<{type}, bool>> predicate)");
        AppendLine($"            => new global::Pragmatic.Persistence.Documents.DocCompiledSpecification<{type}>(Specification_{index}, new object?[] {{ {specification.Captures} }}, predicate);");
        AppendLine();
    }

    private void RenderInterceptor(DocumentInterceptorModel interceptor)
    {
        var index = interceptor.Index;
        if (interceptor.PlanInitializer is { } plan)
        {
            AppendLine($"        private static readonly global::Pragmatic.Persistence.Documents.Planning.DocPlan<{interceptor.PlanEntity}> Plan_{index} = {plan};");
            if (interceptor.JoinInitializer is { } join)
                AppendLine($"        private static readonly global::Pragmatic.Persistence.Documents.Planning.DocJoin<{interceptor.JoinInner}> Join_{index} = {join};");
            AppendLine();
        }

        AppendLine($"        {interceptor.Attribute}");
        AppendLine($"        internal static {interceptor.Returns} {interceptor.Name}_{index}{interceptor.TypeParameters}({interceptor.Parameters}) => {interceptor.Body};");
        AppendLine();
    }
}
