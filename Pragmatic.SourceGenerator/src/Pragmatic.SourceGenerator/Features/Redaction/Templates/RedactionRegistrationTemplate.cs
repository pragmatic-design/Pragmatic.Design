using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Redaction.Templates;

/// <summary>
///     Emits the DI registration for this assembly's <c>GeneratedRedactionMap</c>.
/// </summary>
/// <remarks>
///     <c>TryAddEnumerable</c> on purpose: it de-duplicates by implementation type, so every module's
///     map composes and an application that registers one of its own is added beside them rather than
///     suppressed. That is the extension point <c>IRedactionMap</c> documents.
/// </remarks>
internal sealed class RedactionRegistrationTemplate(string mapNamespace, string assemblyName) : CSharpTemplate
{
    // The same helper the metadata entry uses, so the host cannot be told to call a name this does
    // not emit.
    private readonly string _extensionClass = GeneratedRegistrationNames.RedactionRegistrationClass(assemblyName);

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Redaction";
    protected override string? TriggerInfo => "[NotLogged] / [PersonalData]";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Redaction", "Registration"),
        ToSourceText());

    protected override bool Validate() => true;

    public override void RenderFile()
    {
        AppendLine($"namespace {mapNamespace};");
        AppendLine();
        XmlSummary("Registers this assembly's declared-redaction map.");
        Class(_extensionClass, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        XmlSummary("Adds this assembly's [NotLogged] / [PersonalData] map to the redaction channel.");
        Method("AddGeneratedRedactionMap", () =>
        {
            AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions" +
                ".TryAddEnumerable(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor" +
                $".Singleton<global::Pragmatic.Serialization.IRedactionMap, global::{mapNamespace}.GeneratedRedactionMap>());");
            AppendLine("return services;");
        },
        "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
        [
            new()
            {
                Type = "this global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
                Name = "services",
            },
        ],
        AccessModifier.Public,
        new MethodModifiers { IsStatic = true });
    }
}
