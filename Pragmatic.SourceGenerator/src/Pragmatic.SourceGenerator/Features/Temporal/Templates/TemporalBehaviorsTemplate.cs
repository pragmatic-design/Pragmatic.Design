using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Temporal.Models;

namespace Pragmatic.SourceGenerator.Features.Temporal.Templates;

/// <summary>
///     Generates the per-assembly <c>_Infra.Temporal.Behaviors.g.cs</c>: one IServiceCollection
///     extension that registers every attributed DTO property into
///     <c>TemporalJsonBehaviorRegistry</c> (zero reflection — plain generated calls).
/// </summary>
internal sealed class TemporalBehaviorsTemplate : CSharpTemplate
{
    private const string RegistryFqn = "global::Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehaviorRegistry";
    private const string BehaviorEnumFqn = "global::Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehavior";

    private readonly ImmutableArray<TemporalBehaviorPropertyModel> _models;
    private readonly string _namespacePrefix;
    private readonly string _targetNamespace;
    private readonly string _className;
    private readonly string _methodName;

    public TemporalBehaviorsTemplate(ImmutableArray<TemporalBehaviorPropertyModel> models)
    {
        _models = models;
        _namespacePrefix = DeriveNamespacePrefix(models);
        _targetNamespace = TargetNamespaceOf(models, _namespacePrefix);

        var prefixIdentifier = PrefixIdentifierOf(_namespacePrefix);
        _className = GeneratedRegistrationNames.TemporalBehaviorsClass(prefixIdentifier);
        _methodName = GeneratedRegistrationNames.TemporalBehaviorsMethod(prefixIdentifier);
    }

    /// <summary>Prefix derived from the model namespaces (used by the metadata template).</summary>
    public string NamespacePrefix => _namespacePrefix;

    /// <summary>FQN "Namespace.Class.Method" the Composition host invokes via metadata aggregation.</summary>
    public string RegistrationMethodFqn =>
        GeneratedRegistrationNames.TemporalBehaviorsFqn(_targetNamespace, PrefixIdentifierOf(_namespacePrefix));

    /// <summary>
    ///     The same FQN, without rendering: the local-registration channel a host-declared
    ///     <c>[ToClientTimezone]</c> property travels through needs the name, not the file.
    /// </summary>
    public static string RegistrationMethodFqnFor(ImmutableArray<TemporalBehaviorPropertyModel> models)
    {
        var namespacePrefix = DeriveNamespacePrefix(models);
        return GeneratedRegistrationNames.TemporalBehaviorsFqn(
            TargetNamespaceOf(models, namespacePrefix), PrefixIdentifierOf(namespacePrefix));
    }

    private static string TargetNamespaceOf(
        ImmutableArray<TemporalBehaviorPropertyModel> models,
        string namespacePrefix)
        => models
            .Select(m => m.ContainingNamespace)
            .FirstOrDefault(ns => !string.IsNullOrEmpty(ns)) ?? namespacePrefix;

    private static string PrefixIdentifierOf(string namespacePrefix)
        => string.IsNullOrEmpty(namespacePrefix) ? "" : NamespacePrefixHelper.ToIdentifier(namespacePrefix);

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Temporal";
    protected override string? SourceInfo => $"Timezone behaviors for {_models.Length} propert{(_models.Length == 1 ? "y" : "ies")}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Temporal", "Behaviors"),
        ToSourceText());

    protected override bool Validate() => _models.Length > 0;

    public override void RenderFile()
    {
        AddUsings("Microsoft.Extensions.DependencyInjection");

        if (!string.IsNullOrEmpty(_targetNamespace))
        {
            AppendNamespace(_targetNamespace);
            AppendLine();
        }

        XmlSummary($"Registers the timezone conversion behaviors declared in this assembly ({_models.Length} total).");

        Class(_className, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    private void RenderBody()
    {
        var returnType = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
        var parameters = new List<MethodParameter>
        {
            new("this " + returnType, "services")
        };

        Method(_methodName, RenderMethodBody, returnType, parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderMethodBody()
    {
        foreach (var model in _models
                     .OrderBy(m => m.ContainingTypeFqn)
                     .ThenBy(m => m.PropertyName))
        {
            AppendLine(
                $"{RegistryFqn}.Register(typeof(global::{model.ContainingTypeFqn}), \"{model.PropertyName}\", {BehaviorEnumFqn}.{model.Behavior});");
        }

        AppendLine();
        AppendLine("return services;");
    }

    private static string DeriveNamespacePrefix(ImmutableArray<TemporalBehaviorPropertyModel> models)
    {
        var namespaces = models
            .Select(m => m.ContainingNamespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .ToList();

        return namespaces.Count == 0 ? "" : NamespacePrefixHelper.DerivePrefix(namespaces);
    }
}
