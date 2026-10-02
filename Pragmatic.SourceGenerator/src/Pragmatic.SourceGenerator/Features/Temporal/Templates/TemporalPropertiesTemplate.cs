using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Temporal.Models;

namespace Pragmatic.SourceGenerator.Features.Temporal.Templates;

/// <summary>
///     Registers a type's temporal properties with <c>TemporalPropertyRegistry</c> at module load.
/// </summary>
/// <remarks>
///     Replaces the scan both EF conventions performed at startup:
///     <c>ClrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)</c> for every entity,
///     testing each property against a closed set of eight temporal types. Closed and known at compile
///     time means the list can be written instead of discovered — the comment calling that scan "EF
///     model building, the sanctioned exception" was wrong twice: it is our code, and it is decidable.
/// </remarks>
internal sealed class TemporalPropertiesTemplate : CSharpTemplate
{
    private readonly TemporalEntityModel _model;

    public TemporalPropertiesTemplate(TemporalEntityModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Temporal";
    protected override string? SourceInfo => _model.FullyQualifiedName;
    protected override string? TriggerInfo => $"{_model.Properties.Length} temporal propert(ies)";

    protected override bool Validate() => _model.Properties.Length > 0;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.UniqueName, "TemporalProperties", _model.Namespace),
            ToSourceText());

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Registers the temporal properties of {_model.TypeName}.");
        Class(NamingHelper.AppendSuffix(_model.UniqueName, "TemporalProperties"), RenderBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine("internal static void Register()");
        Block(() =>
        {
            AppendLine("global::Pragmatic.Temporal.EntityFrameworkCore.Conventions.TemporalPropertyRegistry.Register(");
            IncreaseIndent();
            AppendLine($"typeof({_model.FullyQualifiedName}),");

            for (var i = 0; i < _model.Properties.Length; i++)
            {
                var property = _model.Properties[i];
                var terminator = i == _model.Properties.Length - 1 ? ");" : ",";
                AppendLine(
                    "new global::Pragmatic.Temporal.EntityFrameworkCore.Conventions.TemporalPropertyEntry("
                    + $"\"{StringHelper.CSharpLiteral(property.Name)}\", typeof({property.TypeName})){terminator}");
            }

            DecreaseIndent();
        });
    }
}
