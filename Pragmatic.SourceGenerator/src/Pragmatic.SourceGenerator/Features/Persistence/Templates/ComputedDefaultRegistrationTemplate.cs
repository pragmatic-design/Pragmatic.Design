using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Registers every <c>[ComputedDefault]</c> generator and the binding the unit of work applies at the save, plus the Persistence metadata
///     entry that makes the generated host call the registration (#740).
/// </summary>
/// <remarks>
///     <para>
///         Scoped, the generator and the binding both: the binding is resolved from the unit of work's scope, the operation's, so a generator
///         may take a scoped service (the current user, a repository). It was a singleton, resolved by the create mutation's invoker, and the
///         container refused a generator that depended on a scoped service.
///     </para>
///     <para>
///         The binding is the one <c>[GeneratedValue]</c> already goes through, which every engine's unit of work applies to the entities a
///         save adds: an entity added by a domain action, an import or a handler gets its default as one created by a mutation does.
///     </para>
/// </remarks>
internal sealed class ComputedDefaultRegistrationTemplate : CSharpTemplate
{
    private const string ContainerName = "ComputedDefaultRegistrationExtensions";

    private readonly ImmutableArray<ComputedDefaultModel> _defaults;
    private readonly string _namespacePrefix;

    public ComputedDefaultRegistrationTemplate(ImmutableArray<ComputedDefaultModel> defaults)
    {
        _defaults = defaults;
        _namespacePrefix = NamespacePrefixHelper.DerivePrefix(defaults.Select(d => d.Namespace));
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    protected override string? SourceInfo => $"Computed default registration for {_defaults.Length} properties";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Persistence", "ComputedDefaults"), ToSourceText());

    protected override bool Validate() => _defaults.Length > 0;

    /// <summary>The fully-qualified method this template emits, or <c>null</c> when the assembly declares no computed default.</summary>
    public static string? RegistrationMethodFor(ImmutableArray<ComputedDefaultModel> defaults)
    {
        if (defaults.Length == 0)
            return null;

        var prefix = NamespacePrefixHelper.DerivePrefix(defaults.Select(d => d.Namespace));
        var container = string.IsNullOrEmpty(prefix) ? ContainerName : $"{prefix}.{ContainerName}";
        return $"{container}.{MethodNameFor(prefix)}";
    }

    private static string MethodNameFor(string prefix)
        => string.IsNullOrEmpty(prefix) ? "AddComputedDefaults" : $"Add{NamespacePrefixHelper.ToIdentifier(prefix)}ComputedDefaults";

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        RenderMetadataEntry();

        if (!string.IsNullOrEmpty(_namespacePrefix))
            AppendNamespace(_namespacePrefix);
        AppendLine();

        XmlSummary("Auto-generated [ComputedDefault] DI registration.");
        Class(ContainerName, () =>
        {
            XmlSummary("Registers every computed-default generator and the binding that applies it at the save.");
            XmlParam("services", "The service collection.");
            XmlReturns("The service collection for chaining.");
            Method(MethodNameFor(_namespacePrefix), RenderMethodBody,
                "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
                [new("this global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")],
                modifiers: new MethodModifiers { IsStatic = true });
        },
        modifiers: new ClassModifiers { IsStatic = true });
    }

    /// <summary>A Persistence metadata entry of its own, as the generated values' is: the host calls the registration method of every one.</summary>
    private void RenderMetadataEntry()
    {
        var method = RegistrationMethodFor(_defaults);
        if (method is null)
            return;

        AppendLine();
        AppendLine($"[assembly: PragmaticMetadata(MetadataCategory.Persistence, \"{MetadataSchemaVersions.Persistence}\", \"\"\"");
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
        // Registered and resolved by its own type, once: IDefaultValueGenerator<TEntity, TValue> cannot tell apart two properties of one entity
        // and one type with two generators, and two properties sharing a generator need it once.
        foreach (var type in _defaults.Select(d => d.GeneratorTypeFqn).Distinct().OrderBy(t => t, System.StringComparer.Ordinal))
            AppendLine($"services.AddScoped<{type}>();");

        AppendLine();
        foreach (var d in _defaults.OrderBy(d => d.EntityFqn + "." + d.PropertyName, System.StringComparer.Ordinal))
        {
            var write = d.SetterName == d.PropertyName ? $"e.{d.PropertyName} = v" : $"e.{d.SetterName}(v)";
            var isEmpty = d.IsString
                ? "static v => string.IsNullOrEmpty(v)"
                : $"static v => global::System.Collections.Generic.EqualityComparer<{d.ValueTypeFqn}>.Default.Equals(v, default!)";

            AppendLine("services.AddScoped<global::Pragmatic.Persistence.Lifecycle.IGeneratedValueBinding>("
                       + $"sp => new global::Pragmatic.Persistence.Lifecycle.GeneratedValueBinding<{d.EntityFqn}, {d.ValueTypeFqn}>(");
            IncreaseIndent();
            AppendLine($"\"{d.PropertyName}\",");
            AppendLine($"global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{d.GeneratorTypeFqn}>(sp),");
            AppendLine($"static e => e.{d.PropertyName},");
            AppendLine($"static (e, v) => {write},");
            AppendLine($"{isEmpty}));");
            DecreaseIndent();
            AppendLine();
        }

        AppendLine("return services;");
    }
}
