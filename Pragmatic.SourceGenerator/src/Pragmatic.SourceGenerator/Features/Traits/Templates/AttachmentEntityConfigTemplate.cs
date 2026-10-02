using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class AttachmentEntityConfigTemplate : CSharpTemplate
{
    private readonly AttachmentTraitModel _model;
    public AttachmentEntityConfigTemplate(AttachmentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasAttachments] config for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForEntityConfig(_model.AttachmentTypeName, _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.EntityFrameworkCore.Metadata.Builders");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();
        XmlSummary($"SG-generated EF Core configuration for <see cref=\"{_model.AttachmentTypeName}\"/>.");
        Class($"{_model.AttachmentTypeName}EntityConfig", RenderConfig,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true },
            interfaces: [$"IEntityTypeConfiguration<{_model.AttachmentTypeName}>"]);
    }

    private void RenderConfig()
    {
        Method("Configure", RenderBody,
            accessModifier: AccessModifier.Public,
            returnType: "void",
            parameters: [new MethodParameter($"EntityTypeBuilder<{_model.AttachmentTypeName}>", "builder")]);
    }

    private void RenderBody()
    {
        // Primary key — Id property mapped to the "PersistenceId" column (Pragmatic convention,
        // and what the migrations schema emits for this table). The key stays value-generated:
        // the SG-generated upload action returns attachment.Id straight after Add(), so EF's
        // client-side Guid generator has to have run — ValueGeneratedNever would hand out Guid.Empty.
        AppendLine("builder.HasKey(e => e.Id);");
        AppendLine("builder.Property(e => e.Id).HasColumnName(\"PersistenceId\");");
        AppendLine("builder.Ignore(e => e.PersistenceId); // Computed alias — not a separate column");
        AppendLine($"builder.Ignore(e => e.ParentEntityId); // Abstract FK from AttachmentBase — concrete FK is {_model.ParentFkPropertyName}");
        AppendLine();
        AppendLine($"builder.HasOne(e => e.{_model.ParentTypeName})");
        AppendLine("    .WithMany(p => p.Attachments)");
        AppendLine($"    .HasForeignKey(e => e.{_model.ParentFkPropertyName})");
        AppendLine("    .OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);");
        AppendLine();
        AppendLine("builder.Property(e => e.FileName).HasMaxLength(512).IsRequired();");
        AppendLine("builder.Property(e => e.ContentType).HasMaxLength(256).IsRequired();");
        AppendLine("builder.Property(e => e.StorageUri).HasMaxLength(2048).IsRequired();");
        // Nullable beside a required StorageUri, and the pair is the contract: the original is the
        // record, the thumbnail is derived from it. Null is the ordinary case — a PDF has none, and
        // neither has anything uploaded before a thumbnail was declared.
        AppendLine("builder.Property(e => e.ThumbnailUri).HasMaxLength(2048);");
        AppendLine("builder.Property(e => e.Description).HasMaxLength(1000);");
        AppendLine("builder.Property(e => e.UploadedBy).HasMaxLength(256).IsRequired();");
        AppendLine("builder.Property(e => e.DeletedBy).HasMaxLength(256);");
        AppendLine();
        // Named, and not the anonymous overload: EF refuses a type carrying both, and the DbContext
        // adds the named "ParentTenant" filter to every trait child of a tenant-scoped parent.
        AppendLine("builder.HasQueryFilter(\"SoftDelete\", e => !e.IsDeleted);");
        AppendLine($"builder.HasIndex(e => e.{_model.ParentFkPropertyName});");
    }
}
