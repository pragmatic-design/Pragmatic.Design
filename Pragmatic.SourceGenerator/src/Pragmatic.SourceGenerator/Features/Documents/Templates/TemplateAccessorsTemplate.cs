using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Documents.Models;

namespace Pragmatic.SourceGenerator.Features.Documents.Templates;

/// <summary>
///     Emits <c>_Infra.Templating.Accessors.g.cs</c>: the property getters of every type this assembly
///     hands a template with its type known, registered with <c>TemplateAccessors</c> at module load.
/// </summary>
/// <remarks>
///     <c>TemplateDataContext</c> read every such property with <c>PropertyInfo.GetValue</c>, after two
///     dictionary lookups keyed by type and name, and under Native AOT could not read it at all. The type
///     and its properties are known here, so the getter is written: one dictionary lookup by name and a
///     direct call.
/// </remarks>
internal sealed class TemplateAccessorsTemplate(string generatedNamespace, EquatableArray<TemplateDataTypeModel> types)
    : CSharpTemplate
{
    private const string Registry = "global::Pragmatic.Documents.Templating.Data.TemplateAccessors";

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Documents";

    protected override string? TriggerInfo => $"{types.Length} type(s) given to a template";

    protected override bool Validate() => types.Length > 0;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Templating", "Accessors"), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(generatedNamespace);
        AppendLine();

        XmlSummary("Registers the property accessors of the types this assembly gives a template.");
        Class("PragmaticTemplateAccessors", RenderBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine("internal static void Register()");
        Block(() =>
        {
            foreach (var type in types)
            {
                var name = type.FullyQualifiedName;
                AppendLine($"{Registry}.Register<{name}>(");
                IncreaseIndent();
                AppendLine($"new global::System.Collections.Generic.Dictionary<string, global::System.Func<{name}, object?>>");
                AppendLine("{");
                IncreaseIndent();

                foreach (var property in type.Properties)
                    AppendLine($"[\"{StringHelper.CSharpLiteral(property)}\"] = static value => value.@{property},");

                DecreaseIndent();
                AppendLine("});");
                DecreaseIndent();
            }
        });
    }
}
