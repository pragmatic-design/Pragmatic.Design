using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator;

/// <summary>
///     Emits one <c>[assembly: PendingContract("Type")]</c> per endpoint whose operation body is still
///     <c>throw Behavior.Pending()</c>. Consumed by the contract-test generator to skip not-yet-implemented
///     endpoints (see <see cref="PendingContractGenerator"/>).
/// </summary>
internal sealed class PendingContractsTemplate : CSharpTemplate
{
    private readonly IReadOnlyList<string> _typeNames;

    public PendingContractsTemplate(IReadOnlyList<string> typeNames)
    {
        _typeNames = typeNames;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/PendingContracts";

    public override Artifact RenderOutput() =>
        new(VirtualFolderHints.ForMetadata("PendingContracts"), ToSourceText());

    protected override bool Validate() => _typeNames.Count > 0;

    public override void RenderFile()
    {
        foreach (var name in _typeNames)
            AppendLine($"[assembly: global::Pragmatic.Authoring.PendingContract(\"{name}\")]");
    }
}
