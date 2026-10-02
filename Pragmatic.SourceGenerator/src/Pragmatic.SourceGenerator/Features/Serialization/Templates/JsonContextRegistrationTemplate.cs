using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Serialization.Templates;

/// <summary>
///     Emits <c>_Infra.Json.Registration.g.cs</c> — registers the generated context into the shared
///     seam. Called by the host via the JsonContexts metadata aggregation.
/// </summary>
internal sealed class JsonContextRegistrationTemplate : CSharpTemplate
{
    private readonly string _namespace;

    public JsonContextRegistrationTemplate(string ns) => _namespace = ns;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Serialization";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Json", "Registration"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AppendLine();
        AppendLine($"namespace {GeneratedRegistrationNames.InGeneratedNamespace(_namespace)};");
        AppendLine();

        XmlSummary("Registers the generated JsonSerializerContext into the Pragmatic seam.");
        AppendLine($"public static class {GeneratedRegistrationNames.JsonContextClass}");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {GeneratedRegistrationNames.JsonContextMethod}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        IncreaseIndent();
        AppendLine("=> global::Pragmatic.Serialization.PragmaticJsonServiceCollectionExtensions.AddPragmaticJsonContext(services, PragmaticJsonContext.Default);");
        DecreaseIndent();
        DecreaseIndent();
        AppendLine("}");
    }
}
