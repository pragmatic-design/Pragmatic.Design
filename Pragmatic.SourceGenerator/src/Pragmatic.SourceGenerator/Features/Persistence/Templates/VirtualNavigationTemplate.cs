using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates virtual navigation extension methods on owner entities
///     for polymorphic attachments and typed temporal relations.
///     Produces Query{X}s(repo) (IQueryable) and Load{X}sBatchAsync(repo, ids, ct) methods.
/// </summary>
internal sealed class VirtualNavigationTemplate : CSharpTemplate
{
    private readonly OwnerTypeModel _owner;
    private readonly PolymorphicAttachmentModel _attachment;

    public VirtualNavigationTemplate(OwnerTypeModel owner, PolymorphicAttachmentModel attachment)
    {
        _owner = owner;
        _attachment = attachment;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/VirtualNavigation";
    protected override string? SourceInfo => $"VirtualNavigation for {_owner.TypeName} → {_attachment.TypeName}";
    protected override string? TriggerInfo => $"[Attachable<{_owner.TypeName}>] on {_attachment.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_owner.TypeName,
            NamingHelper.AppendSuffix(_attachment.TypeName, "Navigation"), _owner.Namespace),
        ToSourceText());

    protected override bool Validate() => !string.IsNullOrEmpty(_owner.TypeName) && _attachment.IsValid;

    // The OwnerType discriminator is the owner's FULL type name (matching typeof(Owner).FullName on the
    // set side), so two same-named owners in different namespaces don't collide.
    private string OwnerDiscriminator => _owner.FullTypeName.StartsWith("global::", System.StringComparison.Ordinal)
        ? _owner.FullTypeName.Substring("global::".Length)
        : _owner.FullTypeName;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections.Generic");
        AddUsing("System.Linq");
        AddUsing("System.Threading");
        AddUsing("System.Threading.Tasks");

        var ownerNs = _owner.Namespace ?? _attachment.Namespace;
        AppendNamespace(ownerNs);
        AppendLine();

        var className = $"{_owner.TypeName}{_attachment.TypeName}NavigationExtensions";
        XmlSummary($"Virtual navigation extensions for loading {_attachment.TypeName} attachments from {_owner.TypeName}.");

        Class(className, RenderMethods,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderMethods()
    {
        var attachmentType = _attachment.FullTypeName;
        var ownerType = _owner.FullTypeName;
        var idType = $"global::{_owner.IdType}";
        var repoType = $"global::Pragmatic.Persistence.Repository.IReadRepository<{attachmentType}>";
        var queryableType = $"global::System.Linq.IQueryable<{attachmentType}>";

        // Query{Attachment}s — single owner instance, returns IQueryable
        XmlSummary($"Returns a queryable of {_attachment.TypeName} attachments for this {_owner.TypeName}.");
        XmlParam("owner", $"The {_owner.TypeName} instance.");
        XmlParam("repository", $"The {_attachment.TypeName} repository.");
        XmlReturns($"A queryable of {_attachment.TypeName} attachments.");

        var queryParams = new List<MethodParameter>
        {
            new($"this {ownerType}", "owner"),
            new(repoType, "repository")
        };

        var pluralName = NamingHelper.AppendSuffix(_attachment.TypeName, "s");
        ExpressionMethod($"Query{pluralName}",
            $"repository.Query().Where(a => a.OwnerType == \"{OwnerDiscriminator}\" && a.OwnerId == owner.Id.ToString())",
            queryableType,
            queryParams,
            modifiers: new MethodModifiers { IsStatic = true });

        AppendLine();

        // Load{Attachment}sBatchAsync — batch for multiple owner IDs
        XmlSummary($"Loads {_attachment.TypeName} attachments for multiple {_owner.TypeName} IDs, grouped by owner.");
        XmlParam("repository", $"The {_attachment.TypeName} repository.");
        XmlParam("ownerIds", $"The {_owner.TypeName} IDs to load attachments for.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns($"A lookup of {_attachment.TypeName} grouped by owner ID string.");

        var batchParams = new List<MethodParameter>
        {
            new(repoType, "repository"),
            new($"global::System.Collections.Generic.IEnumerable<{idType}>", "ownerIds"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method($"Load{pluralName}BatchAsync", RenderBatchBody,
            $"async global::System.Threading.Tasks.Task<global::System.Linq.ILookup<string, {attachmentType}>>",
            batchParams,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderBatchBody()
    {
        var attachmentType = _attachment.FullTypeName;
        AppendLine("var ids = ownerIds.Select(id => id.ToString()).ToList();");
        AppendLine($"var items = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        IncreaseIndent();
        AppendLine($"repository.Query()");
        AppendLine($"    .Where(a => a.OwnerType == \"{OwnerDiscriminator}\" && ids.Contains(a.OwnerId)),");
        AppendLine("ct);");
        DecreaseIndent();
        AppendLine("return items.ToLookup(a => a.OwnerId);");
    }
}
