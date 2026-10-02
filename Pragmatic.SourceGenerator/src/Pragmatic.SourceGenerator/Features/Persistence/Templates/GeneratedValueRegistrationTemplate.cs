using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Registers every <c>[GeneratedValue]</c> generator and the binding the unit of work applies,
///     plus the Persistence metadata entry that makes the generated host call the registration.
/// </summary>
/// <remarks>
///     <para>
///         The registration is derived from the entity's own attribute, not from the mutations that
///         carry a computed default: an entity with a generated value and no create mutation would
///         otherwise produce a generator that nothing registers, and the column would stay empty with
///         no error anywhere. The declaration is on the property, so the wiring has to be too.
///     </para>
///     <para>
///         A second <c>[assembly: PragmaticMetadata(Persistence, …)]</c> beside the entity one. The
///         attribute allows multiple and the host calls the registration method of every Persistence
///         entry. <c>EntityMetadataReader</c> reads every entry, not the first: a reader that stopped
///         at the first would see a second one carrying no entities and take the whole assembly as
///         declaring none.
///     </para>
/// </remarks>
internal sealed class GeneratedValueRegistrationTemplate : CSharpTemplate
{
    private const string ContainerName = "GeneratedValueRegistrationExtensions";

    private readonly ImmutableArray<GeneratedValueModel> _values;
    private readonly string _namespacePrefix;

    public GeneratedValueRegistrationTemplate(ImmutableArray<GeneratedValueModel> values)
    {
        _values = values;
        _namespacePrefix = NamespacePrefixHelper.DerivePrefix(values.Select(v => v.Namespace));
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    protected override string? SourceInfo =>
        $"Generated value registration for {_values.Length} properties";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Persistence", "ValueGenerators"), ToSourceText());

    protected override bool Validate() => _values.Length > 0;

    /// <summary>
    ///     The fully-qualified method this template emits, declared on the metadata so the host calls
    ///     it, or <c>null</c> when the assembly declares no generated value.
    /// </summary>
    public static string? RegistrationMethodFor(ImmutableArray<GeneratedValueModel> values)
    {
        if (values.Length == 0)
            return null;

        var prefix = NamespacePrefixHelper.DerivePrefix(values.Select(v => v.Namespace));
        var container = string.IsNullOrEmpty(prefix) ? ContainerName : $"{prefix}.{ContainerName}";

        return $"{container}.{MethodNameFor(prefix)}";
    }

    private static string MethodNameFor(string prefix)
        => string.IsNullOrEmpty(prefix)
            ? "AddGeneratedValueGenerators"
            : $"Add{NamespacePrefixHelper.ToIdentifier(prefix)}ValueGenerators";

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        RenderMetadataEntry();

        if (!string.IsNullOrEmpty(_namespacePrefix))
            AppendNamespace(_namespacePrefix);
        AppendLine();

        XmlSummary("Auto-generated [GeneratedValue] DI registration.");

        Class(ContainerName, () =>
        {
            XmlSummary("Registers every generated-value generator and the binding that applies it.");
            XmlParam("services", "The service collection.");
            XmlReturns("The service collection for chaining.");

            var parameters = new List<MethodParameter>
            {
                new("this global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")
            };

            Method(MethodNameFor(_namespacePrefix), RenderMethodBody,
                "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
                parameters, modifiers: new MethodModifiers { IsStatic = true });
        },
        modifiers: new ClassModifiers { IsStatic = true });
    }

    /// <summary>
    ///     The Persistence metadata entry whose registration method the generated host calls.
    /// </summary>
    /// <remarks>
    ///     A second entry beside the entity one, not a change to it: that entry carries one
    ///     registration method and it is already the query filters'. The payload here is deliberately
    ///     empty — the entities are described once, by the entry that owns them.
    /// </remarks>
    private void RenderMetadataEntry()
    {
        var method = RegistrationMethodFor(_values);
        if (method is null)
            return;

        AppendLine();
        AppendLine(
            "[assembly: PragmaticMetadata(MetadataCategory.Persistence, "
            + $"\"{MetadataSchemaVersions.Persistence}\", \"\"\"");
        AppendLine("{");
        AppendLine("  \"generator\": \"Pragmatic.Persistence.EFCore.SourceGenerator\",");
        AppendLine($"  \"registrationMethod\": \"{method}\",");
        AppendLine("  \"data\": { \"entitiesCount\": 0, \"entities\": [] }");
        AppendLine("}");
        AppendLine("\"\"\")]");
        AppendLine();
    }

    private void RenderMethodBody()
    {
        foreach (var value in _values.OrderBy(
                     v => v.EntityFullName + "." + v.PropertyName, System.StringComparer.Ordinal))
        {
            var entity = $"global::{value.EntityFullName}";
            var generator = Qualify(value.Namespace, value.GeneratorClassName);
            var generatorInterface =
                $"global::Pragmatic.Persistence.Lifecycle.IDefaultValueGenerator<{entity}, string>";

            // Sequence-backed generators inject a boundary-keyed DbContext, so they cannot be
            // singletons; registering the stateless ones the same way keeps the binding's lifetime
            // from having to differ per property.
            AppendLine($"services.AddScoped<{generatorInterface}, {generator}>();");
            AppendLine(
                "services.AddScoped<global::Pragmatic.Persistence.Lifecycle.IGeneratedValueBinding>("
                + "sp => new global::Pragmatic.Persistence.Lifecycle.GeneratedValueBinding<"
                + $"{entity}, string>(");
            IncreaseIndent();
            AppendLine($"\"{value.PropertyName}\",");
            AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
                + $".GetRequiredService<{generatorInterface}>(sp),");
            AppendLine($"static e => e.{value.PropertyName},");

            // A public setter is assigned; a private one is reached through the generated
            // Set{Property} method, which is what SetterName holds when the two differ.
            var write = value.SetterName == value.PropertyName
                ? $"e.{value.PropertyName} = v"
                : $"e.{value.SetterName}(v)";
            AppendLine($"static (e, v) => {write},");
            AppendLine("static v => string.IsNullOrEmpty(v)));");
            DecreaseIndent();
            AppendLine();
        }

        AppendLine("return services;");
    }

    private static string Qualify(string ns, string typeName)
        => string.IsNullOrEmpty(ns) ? $"global::{typeName}" : $"global::{ns}.{typeName}";
}
