using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>
///     Generates [assembly: PragmaticMetadata(MetadataCategory.Validation, ...)] attribute
///     when Pragmatic.Composition is referenced.
/// </summary>
internal sealed class ValidationMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<ValidatorModel> _validators;
    private readonly ImmutableArray<AsyncValidatorBindingsModel> _asyncBindings;
    private readonly bool _indent;
    private readonly string _namespacePrefix;

    public ValidationMetadataTemplate(
        ImmutableArray<ValidatorModel> validators,
        ImmutableArray<AsyncValidatorBindingsModel> asyncBindings,
        bool indent)
    {
        _validators = validators;
        _asyncBindings = asyncBindings;
        _indent = indent;
        _namespacePrefix = ValidatorRegistrationTemplate.NamespacePrefixFor(validators, asyncBindings);
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Validation";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Validation"),
        ToSourceText());

    protected override bool Validate()
    {
        return !_validators.IsDefaultOrEmpty || !_asyncBindings.IsDefaultOrEmpty;
    }

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        var json = BuildJson();

        AppendLine(
            $"[assembly: PragmaticMetadata(MetadataCategory.Validation, \"{MetadataSchemaVersions.Validation}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Validation.SourceGenerator");
        builder.Property("registrationMethod", GeneratedRegistrationNames.ValidatorsFqn(_namespacePrefix));

        builder.Property("data");
        builder.StartObject();

        var validValidators = _validators.IsDefaultOrEmpty
            ? ImmutableArray<ValidatorModel>.Empty
            : _validators.Where(v => !v.MissingValidatorInterface).ToImmutableArray();

        builder.Property("validatorsCount", validValidators.Length);

        if (_indent && !validValidators.IsDefaultOrEmpty)
        {
            builder.Property("validators");
            builder.StartArray();
            foreach (var v in validValidators.OrderBy(v => v.ValidatorFullName))
            {
                builder.StartObject();
                builder.Property("type", v.ValidatorFullName);
                builder.Property("validatedType", v.ValidatedType.FullName);
                builder.EndObject();
            }

            builder.EndArray();
        }

        var asyncBindingsCount = _asyncBindings.IsDefaultOrEmpty ? 0 : _asyncBindings.Length;
        builder.Property("asyncBindingsCount", asyncBindingsCount);

        builder.EndObject();
        builder.EndObject();

        return builder.ToString();
    }
}
