using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for auto-generating entity trait properties: PersistenceId, Id,
///     IAuditable members, and ISoftDelete members. Detects existing manual declarations
///     and skips generation for properties the developer already provides, ensuring
///     backward compatibility with existing entity definitions.
/// </summary>
internal sealed class EntityTraitsTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public EntityTraitsTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Entity] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Traits", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        // Only generate for valid, non-reference entities that need at least one trait
        return _model is { IsValid: true, NeedsTraitGeneration: true };
    }

    public override void RenderFile()
    {
        AddUsing("System");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Auto-generated trait properties for {_model.TypeName}.");

        // Build the list of interfaces to add on the partial declaration
        // Only add interfaces that the developer hasn't already declared
        var interfaces = BuildInterfaceList();

        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true },
            interfaces: interfaces);
    }

    private void RenderBody()
    {
        var needsSeparator = false;

        // PersistenceId + Id — never on a derived entity. The base of a hierarchy is what carries
        // [Entity], so it already declares both; re-emitting them here produces members that hide
        // the base's (CS0108) and an identity the derived type owns a second copy of. Only observable
        // since a hierarchy of declared entities became generable at all.
        if (!_model.HasManualPersistenceId && _model.BaseEntityFullTypeName is null)
        {
            RenderPersistenceIdProperties();
            needsSeparator = true;
        }

        // IAuditable properties
        if (_model is { IsAuditable: true, HasManualAuditableProps: false })
        {
            if (needsSeparator)
                AppendLine();

            RenderAuditableProperties();
            needsSeparator = true;
        }

        // ISoftDelete properties
        if (_model is { IsSoftDelete: true, HasManualSoftDeleteProps: false })
        {
            if (needsSeparator)
                AppendLine();

            RenderSoftDeleteProperties();
        }
    }

    private void RenderPersistenceIdProperties()
    {
        Comment("IEntity");

        AppendLine("public global::System.Guid PersistenceId { get; set; } = global::System.Guid.CreateVersion7();");
        AppendLine();
        AppendLine("public global::System.Guid Id => PersistenceId;");
    }

    private void RenderAuditableProperties()
    {
        Comment("IAuditable");
        AppendLine("public global::System.DateTimeOffset CreatedAt { get; set; }");
        AppendLine("public string? CreatedBy { get; set; }");
        AppendLine("public global::System.DateTimeOffset? UpdatedAt { get; set; }");
        AppendLine("public string? UpdatedBy { get; set; }");
    }

    private void RenderSoftDeleteProperties()
    {
        Comment("ISoftDelete");
        AppendLine("public bool IsDeleted { get; set; }");
        AppendLine("public global::System.DateTimeOffset? DeletedAt { get; set; }");
        AppendLine("public string? DeletedBy { get; set; }");
    }

    /// <summary>
    ///     Builds the interface list for the partial class declaration.
    ///     Only includes interfaces that the developer hasn't already declared on the type.
    /// </summary>
    private List<string> BuildInterfaceList()
    {
        var interfaces = new List<string>();

        // Only add IAuditable if the entity has [Auditable] and doesn't already implement it
        if (_model is { IsAuditable: true, HasManualAuditableInterface: false })
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IAuditable");
        }

        // Only add ISoftDelete if the entity has [SoftDelete] and doesn't already implement it
        if (_model is { IsSoftDelete: true, HasManualSoftDeleteInterface: false })
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.ISoftDelete");
        }

        // [Audited] → IAuditedEntity marker; the AuditLogInterceptor keys off it.
        if (_model.IsAudited)
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IAuditedEntity");
        }

        return interfaces;
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
