using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     What a lambda parameter stands for in SQL. At the root it is the entity under <c>t</c>; after an explicit join it is the
///     result selector's row: a composite of members, each an entity under its alias (<c>Order = o</c>, <c>Customer = c</c>), a
///     column already rendered (<c>Name = c.Name</c>), or another composite (<c>x.Scope</c> carried forward). The composite is
///     never materialised between operators: every member is columns of the one statement, and the reader builds the row
///     type (anonymous or DTO) from them at the terminal.
/// </summary>
internal abstract class RowShape
{
    /// <summary>The columns this shape selects, in reader order.</summary>
    public abstract void Flatten(List<string> columns);

    /// <summary>The C# that builds the shape's value from a reader, its columns starting at <paramref name="ordinal" /> (advanced past them).</summary>
    public abstract string Construct(ref int ordinal);

    /// <summary>The CLR type of the value (null for an entity: its model names it).</summary>
    public abstract ITypeSymbol? Type { get; }

    public abstract bool Optional { get; }
}

/// <summary>An entity's row under an alias; optional when a LEFT JOIN may leave it absent (read as null then).</summary>
internal sealed class EntityRow : RowShape
{
    public EntityRow(EntityModel entity, string alias, bool optional = false, bool derived = false)
    {
        Entity = entity;
        Alias = alias;
        Optional = optional;
        Derived = derived;
    }

    public EntityModel Entity { get; }

    public string Alias { get; }

    public override bool Optional { get; }

    /// <summary>True when the alias is a derived table exposing the entity's columns as <c>cN</c> (a join's filtered inner chain with joins of its own).</summary>
    public bool Derived { get; }

    public override ITypeSymbol? Type => Entity.Type;

    /// <summary>The SQL of one of the entity's columns under this row's alias.</summary>
    public string ColumnSql(ColumnModel column) => Derived ? $"{Alias}.c{Entity.Columns.IndexOf(column)}" : $"{Alias}.\"{column.Column}\"";

    public override void Flatten(List<string> columns) => columns.AddRange(Entity.Columns.Select(ColumnSql));

    public override string Construct(ref int ordinal)
    {
        var start = ordinal;
        ordinal += Entity.Columns.Count;
        var keyOrdinal = start + Math.Max(0, Entity.Columns.FindIndex(c => c.Column == Entity.KeyColumn));
        var built = Entity.Construct("r", "d", i => (start + i).ToString());
        return Optional ? $"(r.IsDBNull({keyOrdinal}) ? null : {built})" : built;
    }
}

/// <summary>A value already rendered as SQL (a column, a function, a CASE), read with the reader its type takes.</summary>
internal sealed class ColumnRow : RowShape
{
    public ColumnRow(string sql, ITypeSymbol type, string reader)
    {
        Sql = sql;
        Type = type;
        Reader = reader;
    }

    public string Sql { get; }

    public override ITypeSymbol? Type { get; }

    /// <summary>The reader with <c>{r}</c>, <c>{i}</c>, <c>{d}</c> to fill in.</summary>
    public string Reader { get; }

    public override bool Optional => false;

    public override void Flatten(List<string> columns) => columns.Add(Sql);

    public override string Construct(ref int ordinal) => Reader.Replace("{r}", "r").Replace("{i}", (ordinal++).ToString()).Replace("{d}", "d");
}

/// <summary>A join prepared but not yet in the statement: a GroupJoin's group, joined when the SelectMany that follows names it.</summary>
internal sealed class PendingJoin
{
    public PendingJoin(EntityModel entity, string alias, string source, List<string> on, bool derived)
    {
        Entity = entity;
        Alias = alias;
        Source = source;
        On = on;
        Derived = derived;
    }

    public EntityModel Entity { get; }

    public string Alias { get; }

    /// <summary>The table, or a derived table as a <c>{dN}</c> piece.</summary>
    public string Source { get; }

    public List<string> On { get; }

    public bool Derived { get; }

    public string Join(bool left) => $"{(left ? "LEFT" : "INNER")} JOIN {Source} {Alias} ON {string.Join(" AND ", On)}";
}

/// <summary>The group of a GroupJoin: not a value, a join waiting for the SelectMany that reads it.</summary>
internal sealed class GroupRow : RowShape
{
    public GroupRow(PendingJoin join) => Join = join;

    public PendingJoin Join { get; }

    public override ITypeSymbol? Type => null;

    public override bool Optional => false;

    public override void Flatten(List<string> columns) => throw new System.InvalidOperationException("a GroupJoin's group is read by the SelectMany that follows");

    public override string Construct(ref int ordinal) => throw new System.InvalidOperationException("a GroupJoin's group is read by the SelectMany that follows");
}

/// <summary>A row of named members: an anonymous object or a DTO with an object initializer.</summary>
internal sealed class CompositeRow : RowShape
{
    public CompositeRow(List<(string Name, RowShape Shape)> members, ITypeSymbol type)
    {
        Members = members;
        Type = type;
    }

    public List<(string Name, RowShape Shape)> Members { get; }

    public override ITypeSymbol? Type { get; }

    public bool IsAnonymous => Type?.IsAnonymousType == true;

    public override bool Optional => false;

    public RowShape? Member(string name)
    {
        foreach (var (memberName, shape) in Members)
        {
            if (memberName == name)
                return shape;
        }

        return null;
    }

    public override void Flatten(List<string> columns)
    {
        foreach (var (_, shape) in Members)
            shape.Flatten(columns);
    }

    /// <summary>The same row without the members of one shape (a GroupJoin's group, once its SelectMany joined it).</summary>
    public CompositeRow Without(RowShape shape) => new(Members.Where(m => !ReferenceEquals(m.Shape, shape)).ToList(), Type!);

    /// <summary>True when a GroupJoin's group sits anywhere in the row: the chain must go on with a SelectMany.</summary>
    public static bool HasGroup(RowShape shape) => shape is GroupRow || (shape is CompositeRow composite && composite.Members.Any(m => HasGroup(m.Shape)));

    public override string Construct(ref int ordinal)
    {
        var parts = new List<string>(Members.Count);
        foreach (var (name, shape) in Members)
            parts.Add($"{name} = {shape.Construct(ref ordinal)}");
        return IsAnonymous ? $"new {{ {string.Join(", ", parts)} }}" : $"new {Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {{ {string.Join(", ", parts)} }}";
    }
}

internal static partial class SqlInterceptorGenerator
{
    internal sealed partial class Projection
    {
        /// <summary>The projection that reads a shape as the chain's row: a join's result selector when no Select follows.</summary>
        public static Projection FromShape(RowShape shape)
        {
            var columns = new List<string>();
            shape.Flatten(columns);
            var ordinal = 0;
            var built = shape.Construct(ref ordinal);
            var isAnonymous = shape is CompositeRow { IsAnonymous: true };
            var type = shape.Type;
            var resultName = isAnonymous ? "TElement" : shape is EntityRow entity ? entity.Entity.FullName : type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new Projection(columns, isAnonymous ? $"(TElement)(object){built}" : built, resultName, isAnonymous, isValueType: !isAnonymous && type?.IsValueType == true);
        }
    }
}
