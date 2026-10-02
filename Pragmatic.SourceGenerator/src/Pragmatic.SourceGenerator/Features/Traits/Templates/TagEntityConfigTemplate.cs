using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates EF Core IEntityTypeConfiguration for the shared Tag entity.
/// Unique index on (Value, Scope), string length constraints.
/// </summary>
internal sealed class TagEntityConfigTemplate : CSharpTemplate
{
    private readonly TagTraitModel _model;

    public TagEntityConfigTemplate(TagTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasTags] tag config for {_model.BoundaryName ?? _model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForEntityConfig(_model.TagTypeName, _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.EntityFrameworkCore.Metadata.Builders");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"EF Core configuration for <see cref=\"{_model.TagTypeName}\"/> (shared tag entity).");
        Class($"{_model.TagTypeName}EntityConfig", RenderConfig,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true },
            interfaces: [$"IEntityTypeConfiguration<{_model.TagTypeName}>"]);
    }

    private void RenderConfig()
    {
        Method("Configure", RenderBody,
            accessModifier: AccessModifier.Public,
            returnType: "void",
            parameters: [new MethodParameter($"EntityTypeBuilder<{_model.TagTypeName}>", "builder")]);
    }

    private void RenderBody()
    {
        // Primary key — Id property mapped to the "PersistenceId" column (Pragmatic convention,
        // and what the migrations schema emits for this table).
        AppendLine("builder.HasKey(e => e.Id);");
        AppendLine("builder.Property(e => e.Id).HasColumnName(\"PersistenceId\");");
        AppendLine("builder.Ignore(e => e.PersistenceId); // Computed alias — not a separate column");
        AppendLine();
        AppendLine("builder.HasIndex(e => new { e.Value, e.Scope })");
        AppendLine("    .IsUnique();");
        AppendLine();
        AppendLine("builder.Property(e => e.Value).HasMaxLength(256).IsRequired();");
        AppendLine("builder.Property(e => e.DisplayValue).HasMaxLength(256).IsRequired();");
        AppendLine("builder.Property(e => e.Scope).HasMaxLength(128);");
        AppendLine("builder.Property(e => e.CreatedBy).HasMaxLength(256);");
    }
}
