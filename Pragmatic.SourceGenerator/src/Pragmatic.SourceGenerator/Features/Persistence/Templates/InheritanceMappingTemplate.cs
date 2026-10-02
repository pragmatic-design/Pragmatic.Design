using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates OnModelCreating inheritance mapping configuration
///     (TPH discriminator, TPT table mapping, TPC concrete tables).
/// </summary>
internal sealed class InheritanceMappingTemplate : CSharpTemplate
{
    private readonly InheritanceMappingModel _model;

    public InheritanceMappingTemplate(InheritanceMappingModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.BaseTypeName} Inheritance ({_model.Strategy}) from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Inheritance] on {_model.BaseTypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.BaseTypeName, "InheritanceMapping", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, HasDerivedTypes: true };

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");

        AppendNamespace(_model.Namespace);
        AppendLine();

        var className = NamingHelper.AppendSuffix(_model.BaseTypeName, "InheritanceConfiguration");

        XmlSummary(
            $"Generated inheritance mapping ({_model.Strategy}) for <see cref=\"{_model.BaseTypeName}\"/> hierarchy.");

        // Public because BoundaryDbContext (generated in the Host project) calls Configure()
        Class(className, RenderBody,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        XmlSummary($"Configures inheritance mapping for the {_model.BaseTypeName} hierarchy.");

        Method("Configure", () =>
        {
            switch (_model.Strategy.ToUpperInvariant())
            {
                case "TPH":
                    RenderTphConfiguration();
                    break;
                case "TPT":
                    RenderTptConfiguration();
                    break;
                case "TPC":
                    RenderTpcConfiguration();
                    break;
            }
        },
        "void",
        new List<MethodParameter>
        {
            new("global::Microsoft.EntityFrameworkCore.ModelBuilder", "modelBuilder")
        },
        modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderTphConfiguration()
    {
        AppendLine($"modelBuilder.Entity<{_model.BaseFullTypeName}>()");
        AppendLine($"    .HasDiscriminator<string>(\"{_model.DiscriminatorColumn}\")");

        foreach (var derived in _model.DerivedTypes)
        {
            var discriminator = derived.DiscriminatorValue ?? derived.TypeName;
            AppendLine($"    .HasValue<{derived.FullTypeName}>(\"{discriminator}\")");
        }

        AppendLine("    ;");
    }

    /// <remarks>
    ///     The table name comes from <see cref="StringHelper.Pluralize" />, the same rule
    ///     <c>EntityConfigurationTemplate</c> uses for a standalone entity. A bare <c>+ "s"</c> here
    ///     would give one product two pluralisation rules that disagree: a <c>Category</c> of its own
    ///     would land in <c>Categories</c>, the same type derived in a TPT hierarchy in
    ///     <c>Categorys</c>.
    /// </remarks>
    private void RenderTptConfiguration()
    {
        foreach (var derived in _model.DerivedTypes)
        {
            AppendLine($"modelBuilder.Entity<{derived.FullTypeName}>().ToTable(\"{StringHelper.Pluralize(derived.TypeName)}\");");
        }
    }

    private void RenderTpcConfiguration()
    {
        AppendLine($"modelBuilder.Entity<{_model.BaseFullTypeName}>().UseTpcMappingStrategy();");
        foreach (var derived in _model.DerivedTypes)
        {
            AppendLine($"modelBuilder.Entity<{derived.FullTypeName}>().ToTable(\"{StringHelper.Pluralize(derived.TypeName)}\");");
        }
    }
}
