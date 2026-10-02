using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Metadata.Sagas.g.cs</c> — assembly metadata for Composition host aggregation.
///     Emits a <c>[assembly: PragmaticMetadata(MetadataCategory.Sagas, ...)]</c> so
///     <c>PragmaticHost.Startup</c> can discover and invoke
///     <c>PragmaticSagaRegistration.AddPragmaticSagas</c> on this assembly.
/// </summary>
internal sealed class SagaMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<SagaModel> _sagas;

    public SagaMetadataTemplate(ImmutableArray<SagaModel> sagas) => _sagas = sagas;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"{_sagas.Length} saga(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Sagas"),
        ToSourceText());

    protected override bool Validate() => !_sagas.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        AppendLine("[assembly: PragmaticMetadata(MetadataCategory.Sagas, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Messaging\",");
        AppendLine("\"registrationMethod\": \"Pragmatic.Messaging.Generated.PragmaticSagaRegistration.AddPragmaticSagas\",");
        AppendLine("\"data\": {");
        IncreaseIndent();
        AppendLine($"\"sagaCount\": {_sagas.Length},");
        AppendLine("\"sagas\": [");
        IncreaseIndent();

        for (var i = 0; i < _sagas.Length; i++)
        {
            var saga = _sagas[i];
            var comma = i < _sagas.Length - 1 ? "," : "";
            var fqn = string.IsNullOrEmpty(saga.Namespace) ? saga.TypeName : $"{saga.Namespace}.{saga.TypeName}";
            AppendLine($"{{ \"type\": \"{fqn}\", \"stateType\": \"{saga.StateTypeFqn}\", \"steps\": {saga.Steps.Length} }}{comma}");
        }

        DecreaseIndent();
        AppendLine("]");
        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }
}
