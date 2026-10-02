using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates EF Core IEntityTypeConfiguration for the junction entity {Parent}Tag.
/// Composite PK: (ParentId, TagId), FKs with cascade/restrict.
/// </summary>
internal sealed class TagJunctionConfigTemplate : CSharpTemplate
{
    private readonly TagTraitModel _model;

    public TagJunctionConfigTemplate(TagTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasTags] junction config for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForEntityConfig(_model.JunctionTypeName, _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.EntityFrameworkCore.Metadata.Builders");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"EF Core configuration for <see cref=\"{_model.JunctionTypeName}\"/> (M:N junction).");
        Class($"{_model.JunctionTypeName}EntityConfig", RenderJunctionConfig,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true },
            interfaces: [$"IEntityTypeConfiguration<{_model.JunctionTypeName}>"]);
    }

    private void RenderJunctionConfig()
    {
        Method("Configure", RenderBody,
            accessModifier: AccessModifier.Public,
            returnType: "void",
            parameters: [new MethodParameter($"EntityTypeBuilder<{_model.JunctionTypeName}>", "builder")]);
    }

    private void RenderBody()
    {
        // Composite key on the two FKs — no surrogate. The migrations schema emits the same key
        // (TraitEntityInfo.KeyColumns), so the junction table has no PersistenceId column.
        AppendLine($"builder.HasKey(e => new {{ e.{_model.ParentFkPropertyName}, e.TagId }});");
        AppendLine($"builder.Ignore(e => e.ParentEntityId); // Abstract FK from EntityTagBase — concrete FK is {_model.ParentFkPropertyName}");
        AppendLine();
        AppendLine($"builder.HasOne(e => e.{_model.ParentTypeName})");
        AppendLine("    .WithMany(p => p.Tags)");
        AppendLine($"    .HasForeignKey(e => e.{_model.ParentFkPropertyName})");
        AppendLine("    .OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);");
        AppendLine();
        AppendLine("builder.HasOne(e => e.Tag)");
        AppendLine("    .WithMany()");
        AppendLine("    .HasForeignKey(e => e.TagId)");
        AppendLine("    .OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);");
        AppendLine();
        AppendLine("builder.HasIndex(e => e.TagId);");
        AppendLine("builder.Property(e => e.AddedBy).HasMaxLength(256);");
    }
}
