using Pragmatic.Persistence.Documents.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Documents.Generator.Templates;

/// <summary>
///     The document chains translated at compile time: every intercepted call, and each terminal's plan, as file-local members, so that only the
///     interceptors reach the plans.
/// </summary>
internal sealed class DocumentInterceptorsTemplate(EquatableArray<DocumentInterceptorModel> interceptors) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Persistence.Documents";

    protected override string? SourceInfo => "the document chains translated at compile time";

    public override Artifact RenderOutput() => new("Pragmatic.Persistence.Documents.g.cs", ToSourceText());

    protected override bool Validate() => interceptors.Count > 0;

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
        AppendLine("    }");
        AppendLine("}");
    }

    private void RenderInterceptor(DocumentInterceptorModel interceptor)
    {
        var index = interceptor.Index;
        if (interceptor.PlanInitializer is { } plan)
        {
            AppendLine($"        private static readonly global::Pragmatic.Persistence.Documents.DocPlan<{interceptor.PlanEntity}> Plan_{index} = {plan};");
            if (interceptor.JoinInitializer is { } join)
                AppendLine($"        private static readonly global::Pragmatic.Persistence.Documents.DocJoin<{interceptor.JoinInner}> Join_{index} = {join};");
            AppendLine();
        }

        AppendLine($"        {interceptor.Attribute}");
        AppendLine($"        internal static {interceptor.Returns} {interceptor.Name}_{index}{interceptor.TypeParameters}({interceptor.Parameters}) => {interceptor.Body};");
        AppendLine();
    }
}
