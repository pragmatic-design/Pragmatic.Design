using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates the endpoint handler partial class with MapEndpoint method.
/// </summary>
/// <remarks>
///     <para>
///         This is a partial class split across multiple files for maintainability:
///         <list type="bullet">
///             <item>
///                 <description>EndpointHandlerTemplate.cs - Core template methods</description>
///             </item>
///             <item>
///                 <description>EndpointHandlerTemplate.MapEndpoint.cs - Main endpoint mapping body</description>
///             </item>
///             <item>
///                 <description>EndpointHandlerTemplate.Instantiation.cs - Endpoint creation code</description>
///             </item>
///             <item>
///                 <description>EndpointHandlerTemplate.ResultHandling.cs - Result handling code</description>
///             </item>
///             <item>
///                 <description>EndpointHandlerTemplate.Configuration.cs - Configuration and metadata</description>
///             </item>
///             <item>
///                 <description>EndpointHandlerTemplate.Helpers.cs - Utility methods</description>
///             </item>
///         </list>
///     </para>
/// </remarks>
internal sealed partial class EndpointHandlerTemplate : CSharpTemplate
{
    private readonly EndpointModel _model;

    public EndpointHandlerTemplate(EndpointModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Endpoint] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            _model.ResolveHintName("Endpoint"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model.IsValid;
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.AspNetCore.Builder");
        AddUsing("Microsoft.AspNetCore.Http");
        AddUsing("Microsoft.AspNetCore.Routing");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Pragmatic.Result");

        if (!_model.ErrorTypes.IsDefaultOrEmpty)
            AddUsing("Pragmatic.Result.Http");
        if (_model.ShouldAutoValidate)
            AddUsing("Pragmatic.Validation");

        AppendNamespace(_model.Namespace);
        AppendLine();

        Class(_model.TypeName, RenderClassBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderClassBody()
    {
        XmlSummary("Maps this endpoint to the route builder.");
        XmlParam("endpoints", "The endpoint route builder.");
        XmlReturns("The route handler builder for further configuration.");

        // Streaming endpoints always use the non-versioned path (PRAG0523 warns on versioned methods).
        var bodyMethod = _model is { HasActionVersioning: true, HasAspVersioning: true, IsStreamingResponse: false }
            ? (Action)RenderVersionedMapEndpointBody
            : RenderMapEndpointBody;

        Method("MapEndpoint", bodyMethod,
            "Microsoft.AspNetCore.Builder.IEndpointConventionBuilder",
            new List<MethodParameter>
            {
                new("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "endpoints")
            },
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }
}
