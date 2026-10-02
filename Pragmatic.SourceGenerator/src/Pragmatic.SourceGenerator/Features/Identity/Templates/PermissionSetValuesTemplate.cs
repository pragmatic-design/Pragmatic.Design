using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Emits one <c>[assembly: PermissionSetValues]</c> per <c>[PermissionSet]</c> list, so a role in
///     another assembly can be catalogued with what the list holds.
/// </summary>
internal sealed class PermissionSetValuesTemplate : CSharpTemplate
{
    private readonly ImmutableArray<PermissionSetModel> _sets;

    public PermissionSetValuesTemplate(ImmutableArray<PermissionSetModel> sets) => _sets = sets;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("PermissionSets"),
        ToSourceText());

    protected override bool Validate() => !_sets.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AppendLine();

        foreach (var set in _sets)
        {
            var values = string.Join(", ", set.Values.AsImmutableArray().Select(Quote));
            AppendLine(
                $"[assembly: global::Pragmatic.Authorization.PermissionSetValues({Quote(set.MemberFqn)}, {values})]");
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
