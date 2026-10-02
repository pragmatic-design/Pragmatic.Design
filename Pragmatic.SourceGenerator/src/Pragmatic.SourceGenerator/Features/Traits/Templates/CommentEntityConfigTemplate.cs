using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates <c>IEntityTypeConfiguration&lt;{Parent}Comment&gt;</c> with FK, indexes, soft-delete filter.
/// </summary>
internal sealed class CommentEntityConfigTemplate : CSharpTemplate
{
    private readonly CommentTraitModel _model;

    public CommentEntityConfigTemplate(CommentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasComments] config for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForEntityConfig(_model.CommentTypeName, _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.EntityFrameworkCore.Metadata.Builders");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated EF Core configuration for <see cref=\"{_model.CommentTypeName}\"/>.");
        Class($"{_model.CommentTypeName}EntityConfig", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true },
            baseType: $"IEntityTypeConfiguration<{_model.CommentTypeName}>");
    }

    private void RenderBody()
    {
        Method("Configure", RenderConfigure,
            accessModifier: AccessModifier.Public,
            returnType: "void",
            parameters: new List<MethodParameter> { new($"EntityTypeBuilder<{_model.CommentTypeName}>", "builder") });
    }

    private void RenderConfigure()
    {
        // Primary key — Id property mapped to "PersistenceId" column (Pragmatic convention)
        AppendLine("builder.HasKey(e => e.Id);");
        AppendLine("builder.Property(e => e.Id).HasColumnName(\"PersistenceId\");");
        AppendLine("builder.Ignore(e => e.PersistenceId); // Computed alias — not a separate column");
        AppendLine("builder.Ignore(e => e.ParentEntityId); // Abstract FK from CommentBase — concrete FK is {Parent}Id");
        AppendLine();

        // FK to parent entity
        AppendLine($"builder.HasOne(e => e.{_model.ParentTypeName})");
        IncreaseIndent();
        AppendLine($".WithMany(p => p.Comments)");
        AppendLine($".HasForeignKey(e => e.{_model.ParentFkPropertyName})");
        AppendLine(".OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);");
        DecreaseIndent();
        AppendLine();

        // Self-referencing FK for replies
        if (_model.AllowReplies)
        {
            AppendLine("builder.HasOne<" + _model.CommentTypeName + ">()");
            IncreaseIndent();
            AppendLine(".WithMany(e => e.Replies)");
            AppendLine(".HasForeignKey(e => e.ReplyToId)");
            AppendLine(".OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);");
            DecreaseIndent();
            AppendLine();
        }

        // String property max lengths
        AppendLine($"builder.Property(e => e.Content).HasMaxLength({_model.MaxLength});");
        AppendLine("builder.Property(e => e.AuthorId).HasMaxLength(256);");
        AppendLine("builder.Property(e => e.AuthorName).HasMaxLength(256);");
        AppendLine("builder.Property(e => e.UpdatedBy).HasMaxLength(256);");
        AppendLine("builder.Property(e => e.DeletedBy).HasMaxLength(256);");
        // CommentBase documents Metadata as capped at 4000 and says the generated configuration is
        // what enforces it. It never did, so the column came out unbounded (text / nvarchar(max)).
        AppendLine("builder.Property(e => e.Metadata).HasMaxLength(4000);");
        AppendLine();

        // Enum conversions (stored as string for readability)
        AppendLine("builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);");
        AppendLine("builder.Property(e => e.Visibility).HasConversion<string>().HasMaxLength(32);");
        AppendLine();

        // Soft-delete query filter + status filter.
        // With moderation on, only Visible comments are readable: PendingApproval must stay hidden
        // until a moderator approves it — that is what RequireApproval promises — and Rejected/Hidden
        // must stay hidden afterwards. The moderate action reaches them via IgnoreQueryFilters.
        // Named, and not the anonymous overload: EF refuses a type carrying both, and the DbContext
        // adds the named "ParentTenant" filter to every trait child of a tenant-scoped parent.
        AppendLine("builder.HasQueryFilter(\"SoftDelete\", e => !e.IsDeleted);");
        if (_model.RequireApproval)
            AppendLine("builder.HasQueryFilter(\"Moderation\", e => e.Status == Pragmatic.Comments.CommentStatus.Visible);");
        AppendLine();

        // Indexes
        AppendLine($"builder.HasIndex(e => e.{_model.ParentFkPropertyName});");
        AppendLine($"builder.HasIndex(e => new {{ e.{_model.ParentFkPropertyName}, e.CreatedAt }});");
        AppendLine("builder.HasIndex(e => e.Status);");

        if (_model.AllowReplies)
            AppendLine("builder.HasIndex(e => e.ReplyToId);");
    }
}
