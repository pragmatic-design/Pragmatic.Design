using System;
using System.Linq;
using System.Reflection;
using Pragmatic.Comments;
using Pragmatic.Notes;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;
using Pragmatic.SourceGen;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     <see cref="TraitColumns" /> is a copy of the trait base classes' stored members, which the generator cannot read (it does not reference their
///     packages). The child an engine persists is declared from it: a member added to a base class and not here would exist on the EF Core child
///     and not on the engine's, so these compare each list with the public properties of its base class, names, order and types.
/// </summary>
public class TraitColumnsTests
{
    [Fact]
    public void The_comments_columns_are_CommentBases_members()
        => AssertMirrors(typeof(CommentBase<Guid>), TraitColumns.Comment(2000));

    [Fact]
    public void The_notes_columns_are_NoteBases_members()
        => AssertMirrors(typeof(NoteBase<Guid>), TraitColumns.Note(4000));

    [Fact]
    public void The_tags_columns_are_TagBases_members()
        => AssertMirrors(typeof(Pragmatic.Tags.TagBase), TraitColumns.Tag());

    [Fact]
    public void The_tag_links_columns_are_EntityTagBases_members()
        => AssertMirrors(typeof(Pragmatic.Tags.EntityTagBase<Guid>), TraitColumns.TagLink());

    private static void AssertMirrors(Type baseClass, EquatableArray<TraitColumnModel> columns)
    {
        var nullability = new NullabilityInfoContext();
        var declared = baseClass.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not ("Id" or "PersistenceId" or "ParentEntityId"))
            .OrderBy(p => p.MetadataToken)
            .Select(p => $"{TypeName(p.PropertyType, nullability.Create(p).ReadState == NullabilityState.Nullable)} {p.Name}")
            .ToList();
        Assert.Equal(declared, columns.Select(c => $"{c.Type} {c.Name}").ToList());
    }

    private static string TypeName(Type type, bool nullable)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return TypeName(underlying, false) + "?";
        var name = type == typeof(string) ? "string" : type == typeof(bool) ? "bool" : type == typeof(int) ? "int" : "global::" + type.FullName;
        return nullable && !type.IsValueType ? name + "?" : name;
    }
}
