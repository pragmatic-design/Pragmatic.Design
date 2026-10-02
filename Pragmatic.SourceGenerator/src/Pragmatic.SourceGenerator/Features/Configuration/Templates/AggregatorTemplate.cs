using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Configuration.Models;

namespace Pragmatic.SourceGenerator.Features.Configuration.Templates;

/// <summary>
/// Generates a single per-assembly extension method that calls all individual Add{Type}Options() methods.
/// </summary>
internal sealed class AggregatorTemplate : CSharpTemplate
{
    private readonly ImmutableArray<ConfigurationModel> _models;
    private readonly string _namespacePrefix;
    private readonly string? _sensitiveClassifierType;

    public AggregatorTemplate(ImmutableArray<ConfigurationModel> models, string? sensitiveClassifierType = null)
    {
        _models = models;
        _namespacePrefix = DeriveNamespacePrefix(models);
        _sensitiveClassifierType = sensitiveClassifierType;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Configuration";
    protected override string? SourceInfo => $"Aggregator for {_models.Length} configuration(s)";

    /// <summary>The class this template emits into, for the same models.</summary>
    private static string ClassNameFor(string prefix)
        => string.IsNullOrEmpty(prefix)
            ? "ConfigurationExtensions"
            : $"{NamespacePrefixHelper.ToIdentifier(prefix)}ConfigurationExtensions";

    /// <summary>The method this template emits, for the same models.</summary>
    private static string MethodNameFor(string prefix)
        => string.IsNullOrEmpty(prefix)
            ? "AddConfiguration"
            : $"Add{NamespacePrefixHelper.ToIdentifier(prefix)}Configuration";

    /// <summary>The namespace this template emits into: the first model's, as <c>RenderFile</c> picks it.</summary>
    private static string NamespaceFor(ImmutableArray<ConfigurationModel> models)
        => models.Select(m => m.Namespace).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";

    /// <summary>
    ///     The <c>Namespace.Class.Method</c> this template will emit for these models.
    /// </summary>
    /// <remarks>
    ///     Derived here, from the same inputs the rendering uses, so a caller that needs to name the
    ///     method cannot compute it a second way and drift. The generator cannot bind a symbol it is
    ///     about to create, which is the recurring reason a producer has to contribute the shape rather
    ///     than let consumers re-derive it.
    /// </remarks>
    public static string FqnFor(ImmutableArray<ConfigurationModel> models)
    {
        var prefix = DeriveNamespacePrefix(models);
        var ns = NamespaceFor(models);
        var member = $"{ClassNameFor(prefix)}.{MethodNameFor(prefix)}";
        return string.IsNullOrEmpty(ns) ? member : $"{ns}.{member}";
    }

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Configuration", "ConfigurationExtensions"),
        ToSourceText());

    protected override bool Validate() => _models.Length > 0;

    public override void RenderFile()
    {
        AddUsings(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.DependencyInjection");

        // The same namespace FqnFor names, so a caller can predict where this lands.
        var ns = NamespaceFor(_models);

        if (!string.IsNullOrEmpty(ns))
        {
            AppendNamespace(ns);
            AppendLine();
        }

        var className = ClassNameFor(_namespacePrefix);

        XmlSummary($"Registers all [Configuration] options in this assembly ({_models.Length} total).");

        Class(className, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    private void RenderBody()
    {
        var methodName = MethodNameFor(_namespacePrefix);

        var returnType = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
        var parameters = new List<MethodParameter>
        {
            new("this " + returnType, "services"),
            new("global::Microsoft.Extensions.Configuration.IConfiguration", "configuration")
        };

        Method(methodName, RenderMethodBody, returnType, parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderMethodBody()
    {
        foreach (var model in _models.OrderBy(m => m.TypeName))
        {
            var extensionClass = model.Namespace is { Length: > 0 }
                ? $"global::{model.Namespace}.{NamingHelper.AppendSuffix(model.TypeName, "ConfigurationExtensions")}"
                : NamingHelper.AppendSuffix(model.TypeName, "ConfigurationExtensions");

            var methodName = $"Add{NamingHelper.AppendSuffix(model.TypeName, "Options")}";
            AppendLine($"{extensionClass}.{methodName}(services, configuration);");
        }

        // Always register a classifier, so that its ABSENCE has exactly one meaning: the generator
        // never ran. The runtime default fails closed on that case, which it can only do
        // safely because "the generator ran and found nothing sensitive" is expressed here instead of
        // being indistinguishable from silence.
        AppendLine();
        if (_sensitiveClassifierType is { Length: > 0 } classifier)
        {
            // Supersedes the default so stores mask [Sensitive] values in audit.
            AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions" +
                ".AddSingleton<global::Pragmatic.Configuration.ISensitiveKeyClassifier, " +
                $"{classifier}>(services);");
        }
        else
        {
            // No [Sensitive] property in this assembly — a real answer, not a missing one.
            AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions" +
                ".AddSingleton<global::Pragmatic.Configuration.ISensitiveKeyClassifier>(services, " +
                "global::Pragmatic.Configuration.NullSensitiveKeyClassifier.Instance);");
        }

        AppendLine();
        AppendLine("return services;");
    }

    private static string DeriveNamespacePrefix(ImmutableArray<ConfigurationModel> models)
    {
        var namespaces = models
            .Select(m => m.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .ToList();

        if (namespaces.Count == 0)
            return "";
        return NamespacePrefixHelper.DerivePrefix(namespaces);
    }
}
