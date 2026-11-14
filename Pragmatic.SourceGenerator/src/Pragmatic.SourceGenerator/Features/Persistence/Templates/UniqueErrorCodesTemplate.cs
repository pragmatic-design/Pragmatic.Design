using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     An entity's <c>IDeclaresUniqueErrorCodes</c> (#728): the code each <c>[Unique(ErrorCode = …)]</c> index declared, for the index a
///     duplicate broke. Emitted only when one of its indexes declares a code.
/// </summary>
/// <remarks>
///     <para>
///         The index is recognized by the properties it covers when the engine found them (EF Core from its model, the Sql engine from the
///         constraint's name), else by the end of the constraint's name, <c>_{Columns}</c>: MongoDB names the index and only the first key
///         of the duplicate, and its collection need not be named as the table is.
///     </para>
///     <para>
///         Every unique index the entity has is a candidate, the domain key and the ones that declare no code included, the longest first:
///         a name ending in <c>_Code</c> is the index on <c>(TenantId, Code)</c> before the one on <c>(Code)</c>, and a domain key's
///         violation is not taken for a <c>[Unique]</c> that shares its last columns.
///     </para>
/// </remarks>
internal sealed class UniqueErrorCodesTemplate : CSharpTemplate
{
    private const string Interface = "global::Pragmatic.Persistence.Entity.IDeclaresUniqueErrorCodes";

    private readonly EntityMetadataModel _model;

    public UniqueErrorCodesTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Unique(ErrorCode = ...)] on {_model.TypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "UniqueErrorCodes", _model.Namespace), ToSourceText());

    // A derived entity's indexes are its base's (EF Core configures none on it), and a referenced one is declared elsewhere.
    protected override bool Validate()
        => _model is { IsValid: true, IsFromReference: false, BaseEntityFullTypeName: null }
           && _model.UniqueIndexes.AsImmutableArray().Any(i => i.ErrorCode is not null);

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true },
            interfaces: [Interface]);
    }

    private void RenderBody()
    {
        AppendLine($"string? {Interface}.UniqueErrorCode(string? fields, string? constraint)");
        Block(() =>
        {
            foreach (var (columns, code) in Candidates())
            {
                var joined = string.Join(", ", columns);
                var suffix = "_" + string.Join("_", columns);
                AppendLine($"if (fields == \"{joined}\" || (fields is null && constraint is not null && constraint.EndsWith(\"{suffix}\", global::System.StringComparison.Ordinal)))");
                IncreaseIndent();
                AppendLine(code is null ? "return null;" : $"return {Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true)};");
                DecreaseIndent();
            }

            AppendLine("return null;");
        });
    }

    private IEnumerable<(ImmutableArray<string> Columns, string? Code)> Candidates()
    {
        var candidates = new List<(ImmutableArray<string> Columns, string? Code)>();
        if (_model.LogicKeys.Length > 0)
            candidates.Add((_model.LogicKeyColumns, null));
        foreach (var index in _model.UniqueIndexes)
            candidates.Add((_model.UniqueIndexColumns(index), index.ErrorCode));

        return candidates.OrderByDescending(c => c.Columns.Length);
    }

    private static AccessModifier ParseAccessibility(string accessibility)
        => accessibility.ToLowerInvariant() switch
        {
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
}
