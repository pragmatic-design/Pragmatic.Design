using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates the OwnerId property, SetOwnerId method, and IOwnedEntity interface
///     for entities with [HasOwner]. Skips generation if the developer already declares OwnerId.
/// </summary>
internal sealed class OwnershipPropertyTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public OwnershipPropertyTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"Ownership for {_model.TypeName}";
    protected override string? TriggerInfo => $"[HasOwner] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Ownership", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() =>
        _model is { IsValid: true, IsOwnedEntity: true, HasManualOwnedEntityProps: false };

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Auto-generated ownership properties for {_model.TypeName}.");

        var interfaces = new List<string>();
        if (!_model.HasManualOwnedEntityInterface)
        {
            // IOwnershipAssignable derives from IOwnedEntity, so naming it covers both. It is what lets
            // the persistence layer stamp an owner on a row inserted without one — the case an action
            // writing through a repository produces, where the mutation invoker never runs.
            interfaces.Add("global::Pragmatic.Persistence.Entity.IOwnershipAssignable");
        }

        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true },
            interfaces: interfaces);
    }

    private void RenderBody()
    {
        Comment("IOwnedEntity");
        AppendLine("public string OwnerId { get; private set; } = string.Empty;");
        AppendLine();

        XmlSummary("Sets the owner identifier. Called automatically by the mutation invoker on entity creation.");
        XmlParam("ownerId", "The user identifier of the entity owner.");
        Method("SetOwnerId", () =>
        {
            AppendLine("OwnerId = ownerId;");
        }, "void", [new MethodParameter("string", "ownerId")], AccessModifier.Internal);

        AppendLine();
        XmlSummary("Assigns the owner when the row is inserted without one (IOwnershipAssignable).");
        XmlParam("ownerId", "The user identifier of the entity owner.");
        // Explicit implementation: reachable through the interface, which the persistence layer holds,
        // and not through the entity — ownership is not reassignable by application code.
        AppendLine("void global::Pragmatic.Persistence.Entity.IOwnershipAssignable.AssignOwner(string ownerId)");
        AppendLine("    => OwnerId = ownerId;");
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }
}
