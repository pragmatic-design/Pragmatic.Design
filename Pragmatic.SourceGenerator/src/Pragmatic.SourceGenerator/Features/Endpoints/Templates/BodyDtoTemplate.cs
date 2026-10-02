using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates a body DTO record for endpoints with body properties.
/// </summary>
internal sealed class BodyDtoTemplate : BodyDtoTemplateBase
{
    private readonly EndpointModel _model;

    public BodyDtoTemplate(EndpointModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Endpoint] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            _model.ResolveHintName("RequestBody"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, NeedsBodyDto: true };
    }

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary($"Request body for the {_model.TypeName} endpoint.");

        foreach (var variant in _model.BodyDtoVariants)
            RenderVariant(variant);
    }
}
