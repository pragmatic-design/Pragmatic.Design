using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates OwnerType/OwnerId properties and ForOwner&lt;T&gt;() query extension
///     for polymorphic attachment entities.
/// </summary>
internal sealed class PolymorphicAttachmentTemplate : CSharpTemplate
{
    private readonly PolymorphicAttachmentModel _model;

    public PolymorphicAttachmentTemplate(PolymorphicAttachmentModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} PolymorphicAttachment from {_model.Namespace}";
    protected override string? TriggerInfo => $"[PolymorphicAttachment] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "PolymorphicAttachment", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        // Partial class with OwnerType + OwnerId properties
        XmlSummary($"Generated polymorphic attachment properties for <see cref=\"{_model.TypeName}\"/>.");

        Class(_model.TypeName, RenderProperties,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true });

        AppendLine();
        AppendLine();

        // Static extension class with ForOwner<T>() method
        var extensionsName = NamingHelper.AppendSuffix(_model.TypeName, "AttachmentExtensions");
        XmlSummary($"Query extensions for <see cref=\"{_model.TypeName}\"/> polymorphic attachment.");

        Class(extensionsName, RenderExtensions,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderProperties()
    {
        XmlSummary("The type discriminator identifying the owner entity type.");
        AppendLine("public string OwnerType { get; set; } = \"\";");
        AppendLine();

        XmlSummary("The ID of the owning entity (stored as string for polymorphic compatibility).");
        AppendLine("public string OwnerId { get; set; } = \"\";");
    }

    private void RenderExtensions()
    {
        XmlSummary("Filters attachments by owner type.");
        XmlParam("query", "The source queryable.");
        XmlReturns("Filtered queryable for the specified owner type.");

        AppendLine($"public static global::System.Linq.IQueryable<{_model.FullTypeName}> ForOwner<TOwner>(this global::System.Linq.IQueryable<{_model.FullTypeName}> query) where TOwner : class");
        Block(() =>
        {
            // Discriminate by the FULL type name, not the short name: two same-named owner types in
            // different namespaces would otherwise share a discriminator and leak each other's
            // attachments. The stored OwnerType must be set to typeof(Owner).FullName to match.
            AppendLine("var ownerTypeName = typeof(TOwner).FullName;");
            // Qualified: the file declares no using, and a consumer without System.Linq among its implicit
            // usings would find no Where on the queryable.
            AppendLine("return global::System.Linq.Queryable.Where(query, a => a.OwnerType == ownerTypeName);");
        });

        // Generate typed convenience methods for each known owner type
        if (_model.HasOwners)
        {
            foreach (var owner in _model.OwnerTypes)
            {
                AppendLine();
                XmlSummary($"Filters attachments owned by {owner.TypeName}.");
                AppendLine($"public static global::System.Linq.IQueryable<{_model.FullTypeName}> For{owner.TypeName}(this global::System.Linq.IQueryable<{_model.FullTypeName}> query)");
                Block(() =>
                {
                    AppendLine($"return query.ForOwner<{owner.FullTypeName}>();");
                });
            }
        }
    }
}
