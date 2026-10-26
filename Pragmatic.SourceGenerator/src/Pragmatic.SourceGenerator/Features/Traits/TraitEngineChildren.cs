using System.Collections.Generic;
using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Traits;

/// <summary>
///     The children of <c>[HasComments]</c> and <c>[HasNotes]</c> where the Sql or documents engine persists the parent: the child as a Pragmatic
///     <c>[Entity]</c> (<see cref="TraitEngineEntityTemplate" />), the filter classes its plans name (<see cref="EntityShapeArtifacts.Filters" />,
///     as for a declared entity), and the parent's collection. The same files go to the engines' binding (<see cref="Sources" />): no generator
///     sees another's output, and the engines read the child off its declarations.
/// </summary>
/// <remarks>
///     ASSUMPTION: a compilation that references EF Core keeps the EF Core child, whatever engine a parent's boundary names; a module that mixes
///     engines is the open item 15 of the porting (a relation between engines), not this one.
/// </remarks>
internal static class TraitEngineChildren
{
    /// <summary>True when the children are the engines': an engine's bridge referenced, EF Core's not.</summary>
    public static bool Apply(DetectedFeatures features)
        => !features.HasPersistenceEFCore && (features.HasPersistenceSql || features.HasPersistenceDocuments);

    /// <summary>A comment's child, its filters and the parent's <c>Comments</c>.</summary>
    public static IEnumerable<Artifact> ForComment(CommentTraitModel model)
    {
        yield return new TraitEngineEntityTemplate(new TraitEngineEntityModel(model.ParentNamespace, model.CommentTypeName, model.ParentTypeName,
            model.ParentFullTypeName, model.ParentFkPropertyName, model.IdType, IsSoftDelete: true, TraitColumns.Comment(model.MaxLength), "[HasComments]")).RenderOutput();
        foreach (var filter in EntityShapeArtifacts.Filters(Shape(model.ParentNamespace, model.CommentTypeName, model.BoundaryName, model.ParentTypeName, model.ParentIsOwned, model.ParentIsScoped)))
            yield return filter;
        yield return new ParentTraitNavigationTemplate(model).RenderOutput();
    }

    /// <summary>A note's child, its filters and the parent's <c>Notes</c>.</summary>
    public static IEnumerable<Artifact> ForNote(NoteTraitModel model)
    {
        yield return new TraitEngineEntityTemplate(new TraitEngineEntityModel(model.ParentNamespace, model.NoteTypeName, model.ParentTypeName,
            model.ParentFullTypeName, model.ParentFkPropertyName, model.IdType, IsSoftDelete: true, TraitColumns.Note(model.MaxLength), "[HasNotes]")).RenderOutput();
        foreach (var filter in EntityShapeArtifacts.Filters(Shape(model.ParentNamespace, model.NoteTypeName, model.BoundaryName, model.ParentTypeName, model.ParentIsOwned, model.ParentIsScoped)))
            yield return filter;
        yield return new ParentNoteNavigationTemplate(model).RenderOutput();
    }

    /// <summary>The files above, as the engines bind them; empty when the children are EF Core's.</summary>
    public static EquatableArray<ShapeSourceModel> Sources(ImmutableArray<CommentTraitModel> comments, ImmutableArray<NoteTraitModel> notes, DetectedFeatures features)
    {
        if (!Apply(features))
            return EquatableArray<ShapeSourceModel>.Empty;
        var sources = new List<ShapeSourceModel>();
        foreach (var comment in comments)
            Add(ForComment(comment));
        foreach (var note in notes)
            Add(ForNote(note));
        return sources.ToEquatableArray();

        void Add(IEnumerable<Artifact> artifacts)
        {
            foreach (var artifact in artifacts)
            {
                if (!artifact.IsEmpty)
                    sources.Add(new ShapeSourceModel(artifact.HintName, artifact.Text));
            }
        }
    }

    /// <summary>What the filter templates read of an entity: its name, its soft delete, its parent (the parent's visibility).</summary>
    private static EntityMetadataModel Shape(string ns, string typeName, string? boundaryName, string parentTypeName, bool parentIsOwned, bool parentIsScoped) => new()
    {
        TypeName = typeName,
        FullTypeName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}",
        Namespace = ns,
        IdType = "System.Guid",
        Accessibility = "public",
        IsValid = true,
        IsFromReference = false,
        BoundaryName = boundaryName,
        IsSoftDelete = true,
        TraitParent = new TraitParentModel(parentTypeName, parentIsOwned, parentIsScoped),
    };
}
