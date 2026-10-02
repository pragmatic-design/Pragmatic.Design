using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating relation-derived properties on entities:
///     FK properties, collection navigations, reference navigations, and FK setters.
///     Output file: {Namespace}.{Entity}.Relations.g.cs
/// </summary>
internal sealed class EntityRelationsTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public EntityRelationsTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Relation.*] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Relations", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, HasGeneratedRelationProperties: true };
    }

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections.Generic");

        // Add usings for referenced entity types
        foreach (var prop in _model.GeneratedRelationProperties)
        {
            var ns = GetNamespace(prop.FullTypeName);
            if (!string.IsNullOrEmpty(ns) && ns != _model.Namespace)
                AddUsing(ns);
        }

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Relation-generated properties for {_model.TypeName}.");

        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        var fks = _model.GeneratedRelationProperties
            .Where(p => p.Kind == RelationPropertyKind.ForeignKey)
            .ToList();

        var collections = _model.GeneratedRelationProperties
            .Where(p => p.Kind == RelationPropertyKind.Collection)
            .ToList();

        var refNavs = _model.GeneratedRelationProperties
            .Where(p => p.Kind == RelationPropertyKind.ReferenceNav)
            .ToList();

        // FK properties
        if (fks.Count > 0)
        {
            AppendLine("#region Foreign Keys");
            AppendLine();
            foreach (var fk in fks)
            {
                RenderForeignKeyProperty(fk);
                AppendLine();
            }

            AppendLine("#endregion");
        }

        // Reference navigation properties
        if (refNavs.Count > 0)
        {
            if (fks.Count > 0)
                AppendLine();
            AppendLine("#region Reference Navigations");
            AppendLine();
            foreach (var nav in refNavs)
            {
                RenderReferenceNavProperty(nav);
                AppendLine();
            }

            AppendLine("#endregion");
        }

        // Collection navigation properties
        if (collections.Count > 0)
        {
            if (fks.Count > 0 || refNavs.Count > 0)
                AppendLine();
            AppendLine("#region Collection Navigations");
            AppendLine();
            foreach (var col in collections)
            {
                RenderCollectionProperty(col);
                AppendLine();
            }

            AppendLine("#endregion");
        }

        // FK setters
        if (fks.Count > 0)
        {
            AppendLine();
            AppendLine("#region FK Setters");
            AppendLine();
            foreach (var fk in fks)
            {
                RenderForeignKeySetter(fk);
                AppendLine();
            }

            AppendLine("#endregion");
        }
    }

    private void RenderForeignKeyProperty(GeneratedRelationPropertyModel fk)
    {
        if (fk.Summary is not null)
            XmlSummary(fk.Summary);

        var qualifiedType = NormalizeTypeName(fk.TypeName);
        // ManyToOne already carries the '?' in TypeName — RelationForeignKeyNaming.Type puts it
        // there — while OneToOne passes the bare id type. Asking the string avoids `Guid??`.
        var nullable = fk.IsOptional && !qualifiedType.EndsWith("?") ? "?" : "";
        AppendLine($"public {qualifiedType}{nullable} {fk.Name} {{ get; private set; }}");
    }

    private void RenderReferenceNavProperty(GeneratedRelationPropertyModel nav)
    {
        if (nav.Summary is not null)
            XmlSummary(nav.Summary);

        // An optional navigation is nullable and has no `= null!`: the null-forgiving initialiser is
        // the promise "this is always there", which is exactly what optional withdraws.
        AppendLine(nav.IsOptional
            ? $"public {nav.TypeName}? {nav.Name} {{ get; set; }}"
            : $"public {nav.TypeName} {nav.Name} {{ get; set; }} = null!;");
    }

    private void RenderCollectionProperty(GeneratedRelationPropertyModel col)
    {
        if (col.Summary is not null)
            XmlSummary(col.Summary);

        AppendLine($"public ICollection<{col.TypeName}> {col.Name} {{ get; set; }} = [];");
    }

    private void RenderForeignKeySetter(GeneratedRelationPropertyModel fk)
    {
        var qualifiedType = NormalizeTypeName(fk.TypeName);
        var nullable = fk.IsOptional && !qualifiedType.EndsWith("?") ? "?" : "";
        XmlSummary($"Sets the {fk.Name} foreign key.");
        AppendLine($"internal void Set{fk.Name}({qualifiedType}{nullable} value) => {fk.Name} = value;");
    }

    private static string GetNamespace(string fullTypeName)
    {
        var lastDot = fullTypeName.LastIndexOf('.');
        return lastDot > 0 ? fullTypeName.Substring(0, lastDot) : "";
    }

    private static string NormalizeTypeName(string typeName)
    {
        return typeName switch
        {
            "System.Guid" or "Guid" => "global::System.Guid",
            "int" or "System.Int32" => "int",
            "long" or "System.Int64" => "long",
            _ when typeName.Contains(".") => $"global::{typeName}",
            _ => typeName
        };
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
