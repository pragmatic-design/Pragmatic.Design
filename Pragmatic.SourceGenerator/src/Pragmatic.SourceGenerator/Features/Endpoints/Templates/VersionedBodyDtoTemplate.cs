using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates versioned body DTO records for DomainAction endpoints with convention versioning.
///     Each version gets a separate record with only the properties available for that version.
/// </summary>
internal sealed class VersionedBodyDtoTemplate : BodyDtoTemplateBase
{
    private readonly EndpointModel _model;

    public VersionedBodyDtoTemplate(EndpointModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Endpoint] versioned body DTOs on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            _model.ResolveHintName("VersionedBody"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, HasActionVersioning: true, HasAspVersioning: true };
    }

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        foreach (var variant in _model.BodyDtoVariants)
        {
            XmlSummary($"Request body for the {_model.TypeName} endpoint (API version {variant.ApiVersion}).");
            RenderVariant(variant);
            AppendLine();
        }
    }
}
