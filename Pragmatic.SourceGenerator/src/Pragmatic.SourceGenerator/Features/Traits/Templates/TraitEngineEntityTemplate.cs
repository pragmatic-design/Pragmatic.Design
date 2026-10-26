using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     A trait's child for the Sql and documents engines: a Pragmatic <c>[Entity]</c> with the shape's key (<c>PersistenceId</c>, <c>Id</c> its
///     alias), the base class's members declared here with their lengths, and the parent's foreign key and navigation.
/// </summary>
/// <remarks>
///     Not the EF Core child (<c>CommentBase&lt;TId&gt;</c> and its configuration): the engines read an entity off its declarations
///     (<c>[Entity]</c>, <c>[SoftDelete]</c>, <c>[MaxLength]</c>), and a host reads them off the module's compiled type, where a configuration
///     class written for EF Core says nothing. The members are the base class's (<see cref="Transforms.TraitColumns" />), so the generated
///     actions and DTOs read the same names whichever engine persists the child. Each is <c>set</c>, where the base class has some <c>init</c>:
///     the documents engine materializes a document member by member, not with an object initializer.
/// </remarks>
internal sealed class TraitEngineEntityTemplate : CSharpTemplate
{
    private readonly TraitEngineEntityModel _model;

    public TraitEngineEntityTemplate(TraitEngineEntityModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"{_model.Trigger} on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "Entity", _model.Namespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary($"SG-generated child of <see cref=\"{_model.ParentTypeName}\"/> for {_model.Trigger}, persisted by the entity's engine.");
        AppendLine("[global::Pragmatic.Persistence.Entity.Entity]");
        if (_model.IsSoftDelete)
            AppendLine("[global::Pragmatic.Persistence.Entity.SoftDelete]");
        // Whose child it is: the engines apply the parent's visibility.
        AppendLine($"[global::Pragmatic.Persistence.Entity.TraitOf<global::{_model.ParentFullTypeName}>]");
        var interfaces = _model.IsSoftDelete
            ? new List<string> { "global::Pragmatic.Persistence.Entity.IEntity", "global::Pragmatic.Persistence.Entity.ISoftDelete" }
            : new List<string> { "global::Pragmatic.Persistence.Entity.IEntity" };
        Class(_model.TypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, Sealed = true },
            interfaces: interfaces);
    }

    private void RenderBody()
    {
        XmlSummary("The key, as on every Pragmatic entity.");
        AppendLine("public global::System.Guid PersistenceId { get; set; } = global::System.Guid.CreateVersion7();");
        AppendLine();
        XmlSummary("The key's alias.");
        AppendLine("public global::System.Guid Id => PersistenceId;");

        foreach (var column in _model.Columns)
        {
            AppendLine();
            if (column.MaxLength is { } length)
                AppendLine($"[global::System.ComponentModel.DataAnnotations.MaxLength({length})]");
            AppendLine(column.Initializer is { } initializer
                ? $"public {column.Type} {column.Name} {{ get; set; }} = {initializer};"
                : $"public {column.Type} {column.Name} {{ get; set; }}");
        }

        AppendLine();
        XmlSummary($"FK to the parent <see cref=\"{_model.ParentTypeName}\"/>.");
        // A keyword type (int, long) is written as it is; global:: only qualifies a name.
        var keyType = _model.ParentKeyType.Contains('.') ? "global::" + _model.ParentKeyType : _model.ParentKeyType;
        AppendLine($"public {keyType} {_model.ParentForeignKey} {{ get; set; }}");
        AppendLine();
        XmlSummary("Navigation to the parent entity.");
        AppendLine($"public global::{_model.ParentFullTypeName}? {_model.ParentTypeName} {{ get; set; }}");
    }
}
