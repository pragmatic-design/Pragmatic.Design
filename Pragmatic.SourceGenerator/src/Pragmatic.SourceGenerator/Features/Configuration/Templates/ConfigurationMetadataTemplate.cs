using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Configuration.Models;

namespace Pragmatic.SourceGenerator.Features.Configuration.Templates;

/// <summary>
///     Generates [assembly: PragmaticMetadata(MetadataCategory.Configuration, ...)] attribute
///     containing compile-time schema for all [Configuration] options classes.
///     This metadata enables Discovery integration, management UI form generation,
///     and pre-deploy validation against known schemas.
/// </summary>
internal sealed class ConfigurationMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<ConfigurationModel> _models;
    private readonly bool _indent;
    private readonly string? _registrationMethodFqn;

    /// <remarks>
    ///     <paramref name="registrationMethodFqn" /> is the generated <c>Namespace.Class.Method</c> that
    ///     contributes this assembly's sections to the catalogue. Without it the payload describes
    ///     sections and names nobody who can register them, and the host has nothing to read from this
    ///     channel.
    /// </remarks>
    public ConfigurationMetadataTemplate(
        ImmutableArray<ConfigurationModel> models, bool indent, string? registrationMethodFqn = null)
    {
        _models = models;
        _indent = indent;
        _registrationMethodFqn = registrationMethodFqn;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Configuration";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Configuration"),
        ToSourceText());

    protected override bool Validate() => !_models.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        var json = BuildJson();

        AppendLine(
            $"[assembly: PragmaticMetadata(MetadataCategory.Configuration, \"{MetadataSchemaVersions.Configuration}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Configuration.SourceGenerator");

        // The channel the host reads to learn which generated Add* contributes this assembly. Without
        // it MetadataReader.ExtractRegistrationMethod finds nothing and the host can invoke nothing —
        // the payload complete and unreachable.
        builder.Property("registrationMethod", _registrationMethodFqn ?? string.Empty);

        var validModels = _models.Where(m => m.IsPartial && !m.IsStaticOrAbstract).ToImmutableArray();

        builder.Property("configurationsCount", validModels.Length);

        builder.Property("configurations");
        builder.StartArray();

        foreach (var model in validModels.OrderBy(m => m.TypeName))
        {
            builder.StartObject();
            builder.Property("type", model.TypeName);

            if (!string.IsNullOrEmpty(model.Namespace))
                builder.Property("namespace", model.Namespace);

            builder.Property("section", model.SectionPath);
            builder.Property("validateOnStart", model.ValidateOnStart);

            if (!model.Properties.IsDefaultOrEmpty)
            {
                builder.Property("properties");
                builder.StartArray();

                foreach (var prop in model.Properties)
                {
                    builder.StartObject();
                    builder.Property("name", prop.Name);
                    builder.Property("type", prop.TypeFullName);

                    if (prop.IsRequired)
                        builder.Property("required", true);

                    if (prop.IsSensitive)
                        builder.Property("sensitive", true);

                    if (!prop.ValidationAttributes.IsDefaultOrEmpty)
                    {
                        builder.Property("validation");
                        builder.StartObject();

                        foreach (var attr in prop.ValidationAttributes)
                            RenderValidationAttribute(builder, attr);

                        builder.EndObject();
                    }

                    builder.EndObject();
                }

                builder.EndArray();
            }

            builder.EndObject();
        }

        builder.EndArray();
        builder.EndObject();

        return builder.ToString();
    }

    private static void RenderValidationAttribute(MetadataJsonBuilder builder, ValidationAttributeModel attr)
    {
        switch (attr.AttributeName)
        {
            case "Required":
                builder.Property("required", true);
                break;

            case "Range" when attr.ConstructorArgs.Length >= 2:
                builder.Property("range");
                builder.StartObject();
                builder.Property("min", attr.ConstructorArgs[0]);
                builder.Property("max", attr.ConstructorArgs[1]);
                builder.EndObject();
                break;

            case "MaxLength" when attr.ConstructorArgs.Length >= 1:
                builder.Property("maxLength", attr.ConstructorArgs[0]);
                break;

            case "MinLength" when attr.ConstructorArgs.Length >= 1:
                builder.Property("minLength", attr.ConstructorArgs[0]);
                break;

            case "StringLength" when attr.ConstructorArgs.Length >= 1:
                builder.Property("stringLength");
                builder.StartObject();
                builder.Property("max", attr.ConstructorArgs[0]);
                var minArg = attr.NamedArgs.FirstOrDefault(a => a.Key == "MinimumLength");
                if (!string.IsNullOrEmpty(minArg.Value))
                    builder.Property("min", minArg.Value);
                builder.EndObject();
                break;

            case "RegularExpression" when attr.ConstructorArgs.Length >= 1:
                builder.Property("pattern", attr.ConstructorArgs[0]);
                break;

            case "EmailAddress":
                builder.Property("format", "email");
                break;

            case "Phone":
                builder.Property("format", "phone");
                break;

            case "Url":
                builder.Property("format", "url");
                break;
        }
    }

}
