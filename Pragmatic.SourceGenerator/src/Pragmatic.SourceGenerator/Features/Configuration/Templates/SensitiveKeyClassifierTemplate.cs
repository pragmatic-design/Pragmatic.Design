using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Configuration.Models;

namespace Pragmatic.SourceGenerator.Features.Configuration.Templates;

/// <summary>
///     Generates a per-assembly <c>ISensitiveKeyClassifier</c> holding the compile-time set of configuration
///     keys marked <c>[Sensitive]</c>, so a store can mask their values in the audit log with zero reflection.
/// </summary>
internal sealed class SensitiveKeyClassifierTemplate : CSharpTemplate
{
    private const string InterfaceType = "global::Pragmatic.Configuration.ISensitiveKeyClassifier";

    private readonly ImmutableArray<ConfigurationModel> _models;
    private readonly string _namespacePrefix;

    public SensitiveKeyClassifierTemplate(ImmutableArray<ConfigurationModel> models)
    {
        _models = models;
        _namespacePrefix = DeriveNamespacePrefix(models);
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Configuration";

    /// <summary>The namespace the classifier is emitted into (the first model's namespace).</summary>
    public string ClassNamespace =>
        _models.Select(m => m.Namespace).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";

    /// <summary>The generated classifier's simple type name (assembly-unique via the namespace prefix).</summary>
    public string ClassName => string.IsNullOrEmpty(_namespacePrefix)
        ? "ConfigurationSensitiveKeyClassifier"
        : $"{NamespacePrefixHelper.ToIdentifier(_namespacePrefix)}ConfigurationSensitiveKeyClassifier";

    /// <summary>Fully-qualified reference to the generated classifier for use in a registration call.</summary>
    public string FullyQualifiedName => ClassNamespace is { Length: > 0 } ns
        ? $"global::{ns}.{ClassName}"
        : $"global::{ClassName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Configuration", "SensitiveKeys"),
        ToSourceText());

    protected override bool Validate() => SensitiveKeys(_models).Length > 0;

    public override void RenderFile()
    {
        var ns = ClassNamespace;
        if (!string.IsNullOrEmpty(ns))
        {
            AppendNamespace(ns);
            AppendLine();
        }

        XmlSummary("Compile-time set of [Sensitive] configuration keys; masks their audit values.");

        Class(ClassName, RenderBody,
            baseType: InterfaceType,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        AppendLine("private static readonly global::System.Collections.Generic.HashSet<string> SensitiveKeys =");
        IncreaseIndent();
        AppendLine("new(global::System.StringComparer.OrdinalIgnoreCase)");
        AppendLine("{");
        IncreaseIndent();
        foreach (var key in SensitiveKeys(_models))
            AppendLine($"\"{key}\",");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
        AppendLine();

        AppendLine("public bool IsSensitive(string key)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (string.IsNullOrEmpty(key))");
        IncreaseIndent();
        AppendLine("return false;");
        DecreaseIndent();
        // Ignore any environment-overlay prefix ("staging/Booking:ApiKey" → "Booking:ApiKey").
        AppendLine("int slash = key.LastIndexOf('/');");
        AppendLine("string logical = slash >= 0 ? key.Substring(slash + 1) : key;");
        AppendLine("return SensitiveKeys.Contains(logical);");
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>The distinct, ordered set of <c>SectionPath:Property</c> keys marked sensitive.</summary>
    internal static ImmutableArray<string> SensitiveKeys(ImmutableArray<ConfigurationModel> models)
    {
        if (models.IsDefaultOrEmpty)
            return ImmutableArray<string>.Empty;

        return models
            .Where(m => m.IsPartial && !m.IsStaticOrAbstract && m.HasSensitive)
            .SelectMany(m => m.Properties
                .Where(p => p.IsSensitive)
                .Select(p => $"{m.SectionPath}:{p.Name}"))
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static string DeriveNamespacePrefix(ImmutableArray<ConfigurationModel> models)
    {
        var namespaces = models
            .Select(m => m.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .ToList();

        return namespaces.Count == 0
            ? ""
            : NamespacePrefixHelper.DerivePrefix(namespaces);
    }
}
