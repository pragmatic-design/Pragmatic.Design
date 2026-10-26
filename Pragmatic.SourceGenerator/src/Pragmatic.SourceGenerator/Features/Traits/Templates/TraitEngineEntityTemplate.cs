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
    protected override string? TriggerInfo => _model.Parent is { } parent ? $"{_model.Trigger} on {parent.TypeName}" : _model.Trigger;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "Entity", _model.Namespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary(_model.Parent is { } owner
            ? $"SG-generated child of <see cref=\"{owner.TypeName}\"/> for {_model.Trigger}, persisted by the entity's engine."
            : $"SG-generated entity for {_model.Trigger}, persisted by the entity's engine.");
        AppendLine("[global::Pragmatic.Persistence.Entity.Entity]");
        if (_model.IsSoftDelete)
            AppendLine("[global::Pragmatic.Persistence.Entity.SoftDelete]");
        if (_model.ConcurrencyAware)
            AppendLine("[global::Pragmatic.Persistence.Entity.ConcurrencyAware]");
        if (_model.Unique.Count > 0)
            AppendLine($"[global::Pragmatic.Persistence.Entity.Unique({string.Join(", ", _model.Unique.Select(u => $"nameof({u})"))})]");
        // Whose child it is: the engines apply the parent's visibility.
        if (_model.Parent is { } parent)
            AppendLine($"[global::Pragmatic.Persistence.Entity.TraitOf<global::{parent.FullTypeName}>]");
        foreach (var rule in _model.Rules)
            AppendLine($"[global::Pragmatic.Persistence.Entity.VisibleWhen<global::{_model.Namespace}.{_model.TypeName}.{rule}>]");
        var interfaces = _model.IsSoftDelete
            ? new List<string> { "global::Pragmatic.Persistence.Entity.IEntity", "global::Pragmatic.Persistence.Entity.ISoftDelete" }
            : new List<string> { "global::Pragmatic.Persistence.Entity.IEntity" };
        // Not sealed, as no Pragmatic entity is: the engines' setters of a non-public member constrain on the entity's type.
        Class(_model.TypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true },
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

        if (_model.ConcurrencyAware)
        {
            AppendLine();
            XmlSummary("The row version, as on every [ConcurrencyAware] Pragmatic entity: a stale write is refused.");
            AppendLine("public ulong RowVersion { get; internal set; }");
        }

        foreach (var reference in _model.References)
        {
            AppendLine();
            XmlSummary($"Navigation to the <see cref=\"{reference.Navigation}\"/> its {reference.Navigation}Id names.");
            AppendLine($"public global::{reference.TargetFullTypeName}? {reference.Navigation} {{ get; set; }}");
        }

        if (_model.Parent is not { } parent)
            return;
        AppendLine();
        XmlSummary($"FK to the parent <see cref=\"{parent.TypeName}\"/>.");
        // A keyword type (int, long) is written as it is; global:: only qualifies a name.
        var keyType = parent.KeyType.Contains('.') ? "global::" + parent.KeyType : parent.KeyType;
        AppendLine($"public {keyType} {parent.ForeignKey} {{ get; set; }}");
        AppendLine();
        XmlSummary("Navigation to the parent entity.");
        AppendLine($"public global::{parent.FullTypeName}? {parent.TypeName} {{ get; set; }}");
    }
}
