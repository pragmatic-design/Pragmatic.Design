using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Generates a compile-time policy registry mapping action types to their RequirePolicy policies.
///     Eliminates Attribute.GetCustomAttributes + Activator.CreateInstance at runtime.
/// </summary>
internal sealed class PolicyRegistryTemplate : CSharpTemplate
{
    private readonly ImmutableArray<PolicyEntry> _entries;
    private readonly string _assemblyNamespace;

    public PolicyRegistryTemplate(ImmutableArray<PolicyEntry> entries, string assemblyNamespace)
    {
        _entries = entries;
        _assemblyNamespace = assemblyNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_entries.Length} actions with policies";
    protected override string? TriggerInfo => "[RequirePolicy<T>] on DomainActions";

    // An empty registry is a legitimate artifact, not a reason to render
    // nothing. It is what says "the generator ran here and found no declarations", which
    // is the only thing that lets the runtime treat an ABSENT registry as generator silence
    // and fail closed on it.
    protected override bool Validate() => true;

    public override Artifact RenderOutput()
    {
        var hintName = VirtualFolderHints.ForAssembly("Actions", "PolicyRegistry");
        return new Artifact(hintName, ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsings("System", "System.Collections.Generic", "Pragmatic.Actions.Pipeline",
            "Pragmatic.Authorization.Policy");

        if (!string.IsNullOrEmpty(_assemblyNamespace))
            AppendNamespace(_assemblyNamespace);

        Class("GeneratedPolicyRegistry", RenderBody,
            interfaces: ["IPolicyRegistry"],
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        // Static dictionary of action type → policy factory
        AppendLine("private static readonly Dictionary<Type, Func<ResourcePolicy>> Factories = new()");
        AppendLine("{");
        IncreaseIndent();
        foreach (var entry in _entries)
            AppendLine($"[typeof({entry.ActionTypeFullName})] = static () => new {entry.PolicyTypeFullName}(),");
        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        ExpressionMethod("GetPolicy",
            "Factories.TryGetValue(actionType, out var factory) ? factory() : null",
            "ResourcePolicy?",
            parameters:
            [
                new MethodParameter { Type = "Type", Name = "actionType" }
            ],
            accessModifier: AccessModifier.Public);
    }

    internal sealed record PolicyEntry(string ActionTypeFullName, string PolicyTypeFullName);
}
