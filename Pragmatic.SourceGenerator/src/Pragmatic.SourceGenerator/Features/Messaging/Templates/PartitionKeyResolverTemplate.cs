using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Infra.Messaging.PartitionKeys.g.cs</c> — a typed
///     <c>IPartitionKeyResolver</c> that maps this assembly's [PartitionKey] message
///     properties to string keys via compile-time pattern matching (zero reflection).
/// </summary>
internal sealed class PartitionKeyResolverTemplate : CSharpTemplate
{
    private readonly ImmutableArray<PartitionKeyModel> _keys;
    private readonly string _assemblyNamespace;

    public PartitionKeyResolverTemplate(ImmutableArray<PartitionKeyModel> keys, string assemblyNamespace)
    {
        _keys = keys;
        _assemblyNamespace = assemblyNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? SourceInfo => "PartitionKeyResolver";
    protected override string? TriggerInfo => $"{_keys.Length} [PartitionKey] propert(ies)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Messaging", "PartitionKeys"),
        ToSourceText());

    protected override bool Validate() => !_keys.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AppendLine($"namespace {_assemblyNamespace}.Generated;");
        AppendLine();

        XmlSummary(
            "AOT-safe partition-key resolver. Maps [PartitionKey] message properties to " +
            "string keys via compile-time pattern matching.");
        Class("GeneratedPartitionKeyResolver", RenderBody,
            interfaces: ["global::Pragmatic.Messaging.IPartitionKeyResolver"],
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        XmlInheritDoc();
        AppendLine("public string? TryGetPartitionKey(object message)");
        IncreaseIndent();
        AppendLine("=> message switch");
        AppendLine("{");
        IncreaseIndent();

        foreach (var key in _keys)
        {
            // Strings pass through; other types stringify with invariant semantics via
            // FormattableString-free ToString() (Guid/int/enum are culture-stable).
            var access = key.IsString
                ? $"typed.{key.PropertyName}"
                : key.IsNullable
                    ? $"typed.{key.PropertyName}?.ToString()"
                    : $"typed.{key.PropertyName}.ToString()";
            AppendLine($"{key.MessageTypeFqn} typed => {access},");
        }

        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
    }
}
