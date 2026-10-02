using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Generates a compile-time permission requirement registry mapping action types
///     to their [RequirePermission] / [RequireAnyPermission] entries.
///     Eliminates Attribute.GetCustomAttribute reflection at runtime.
/// </summary>
internal sealed class PermissionRequirementRegistryTemplate : CSharpTemplate
{
    private readonly ImmutableArray<PermissionEntry> _entries;
    private readonly string _assemblyNamespace;

    public PermissionRequirementRegistryTemplate(ImmutableArray<PermissionEntry> entries, string assemblyNamespace)
    {
        _entries = entries;
        _assemblyNamespace = assemblyNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_entries.Length} actions with permission requirements";
    protected override string? TriggerInfo => "[RequirePermission] / [RequireAnyPermission] on DomainActions";

    // An empty registry is a legitimate artifact, not a reason to render
    // nothing. It is what says "the generator ran here and found no declarations", which
    // is the only thing that lets the runtime treat an ABSENT registry as generator silence
    // and fail closed on it.
    protected override bool Validate() => true;

    public override Artifact RenderOutput()
    {
        var hintName = VirtualFolderHints.ForAssembly("Actions", "PermissionRequirementRegistry");
        return new Artifact(hintName, ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsings("System", "System.Collections.Generic", "Pragmatic.Actions.Pipeline");

        if (!string.IsNullOrEmpty(_assemblyNamespace))
            AppendNamespace(_assemblyNamespace);

        Class("GeneratedPermissionRequirementRegistry", RenderBody,
            interfaces: ["IPermissionRequirementRegistry"],
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        // Static dictionary of action type → permission requirement
        AppendLine("private static readonly Dictionary<Type, PermissionRequirementEntry> Requirements = new()");
        AppendLine("{");
        IncreaseIndent();

        foreach (var entry in _entries)
        {
            var permsArray = string.Join(", ", entry.Permissions.Select(p => $"\"{Escape(p)}\""));
            var requireAll = entry.RequireAll ? "true" : "false";
            AppendLine($"[typeof({entry.ActionTypeFullName})] = new([{permsArray}], {requireAll}),");
        }

        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        ExpressionMethod("GetRequirement",
            "Requirements.TryGetValue(actionType, out var entry) ? entry : null",
            "PermissionRequirementEntry?",
            parameters:
            [
                new MethodParameter { Type = "Type", Name = "actionType" }
            ],
            accessModifier: AccessModifier.Public);
    }

    private static string Escape(string value) => value.Replace("\"", "\\\"");

    internal sealed record PermissionEntry(
        string ActionTypeFullName,
        ImmutableArray<string> Permissions,
        bool RequireAll);
}
