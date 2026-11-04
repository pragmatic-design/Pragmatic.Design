using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A declared query's <c>[Join&lt;T&gt;(ForeignKey = …, TargetKey = …)]</c> (#508): an entity no navigation reaches, read by key. A result property
///     the query's entity answers is read from it; one it does not is read from the joined entity, by its name without the join's alias
///     (<c>StaffDisplayName</c> is <c>AppUser.DisplayName</c> under <c>Alias = "Staff"</c>) or by its own name, as EF Core's step resolves it
///     (<c>KeyJoinProjection</c>). The Sql engine joins in the statement (<see cref="JoinedChain" />); the documents engine reads the joined documents
///     by the keys of the matched ones (<c>DocQueries.Joins</c>).
/// </summary>
internal sealed partial class PragmaticQuery
{
    /// <summary>The joined entity, when the query declares a key join the engines compile; null otherwise.</summary>
    public INamedTypeSymbol? JoinTarget { get; private set; }

    /// <summary>The query's entity's property holding the joined entity's key.</summary>
    public string JoinForeignKey { get; private set; } = "";

    /// <summary>The joined entity's property the foreign key matches.</summary>
    public string JoinTargetKey { get; private set; } = "";

    /// <summary>True for <c>JoinType.Inner</c>: a row whose joined entity is not found (missing, or outside the caller's filters) is dropped.</summary>
    public bool JoinDrops { get; private set; }

    /// <summary>The result's properties read from the joined entity: the result's property, the joined entity's.</summary>
    public Dictionary<string, string> Joined { get; } = [];

    /// <summary>The result's properties the query's entity answers, in declaration order.</summary>
    public List<string> FromRoot { get; } = [];

    /// <summary>
    ///     Reads the query's key join, refusing what the engines do not compile yet, each with its reason. The names are looked up on the two
    ///     entities as <paramref name="shaped" /> declares them: a relation's foreign key (<c>PropertyId</c>) is a member of the shape only.
    /// </summary>
    internal void ReadJoins(Compilation shaped)
    {
        var joins = Type.GetAttributes().Where(a => a.AttributeClass is { Name: "JoinAttribute", TypeArguments.Length: 1 } c
                                                    && c.ContainingNamespace.ToDisplayString() + "." == Attributes).ToList();
        if (joins.Count == 0)
            return;
        if (joins.Count > 1)
        {
            Unsupported ??= "it declares more than one [Join], which is not compiled yet";
            return;
        }

        var join = joins[0];
        if (join.AttributeClass!.TypeArguments[0] is not INamedTypeSymbol declared)
            return;
        var entity = shaped.GetTypeByMetadataName(Entity.ToDisplayString()) ?? Entity;
        var target = shaped.GetTypeByMetadataName(declared.ToDisplayString()) ?? declared;
        var foreignKey = Named(join, "ForeignKey") as string;
        var targetKey = Named(join, "TargetKey") as string ?? "Id";
        var alias = Named(join, "Alias") as string ?? target.Name;
        var kind = Named(join, "Type") is int k ? k : 0;
        string Refuse(string why) => Unsupported ??= $"[Join<{target.Name}>] {why}";

        if (string.IsNullOrEmpty(foreignKey))
            Refuse("reads through a navigation (Via), which [EagerLoad] or [MapProperty(\"Nav.Field\")] reads on the engines; a join by key (ForeignKey) is compiled");
        else if (kind is not (0 or 1))
            Refuse("is not an inner or a left join, the two the engines compile");
        else if (MapsInMemory || View is not null || SymbolEqualityComparer.Default.Equals(Result, Entity))
            Refuse("needs a result of its own to put the joined entity's fields in, read by name (not MapInMemory, not a grouped view, not the entity)");
        else if (Paged)
            Refuse("with a page is not compiled yet: the page and the total are counted over the joined rows");
        else if (KeyJoinProjection.KeyTypeOn(entity, foreignKey!) is not { } foreignKeyType)
            Refuse($"names the foreign key {foreignKey}, which is not a property of {Entity.Name}");
        else if (KeyJoinProjection.KeyTypeOn(target, targetKey) is not { } targetKeyType)
            Refuse($"names the target key {targetKey}, which is not a property of {target.Name}");
        else if (foreignKeyType != targetKeyType)
            Refuse($"joins {Entity.Name}.{foreignKey} to {target.Name}.{targetKey}, which are not of the same type");
        if (Unsupported is not null)
            return;

        foreach (var property in KeyJoinProjection.SettableProperties(Result))
        {
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name is "NotMappedAttribute"))
                continue;
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name is "MapPropertyAttribute"))
            {
                Refuse($"with [MapProperty] on {Result.Name}.{property.Name} is not compiled yet: a joined query reads its result by name");
                return;
            }

            if (KeyJoinProjection.Answers(entity, property.Name))
            {
                FromRoot.Add(property.Name);
                continue;
            }

            var remainder = property.Name.Length > alias.Length && property.Name.StartsWith(alias, System.StringComparison.Ordinal) ? property.Name.Substring(alias.Length) : null;
            var source = remainder is not null && KeyJoinProjection.Answers(target, remainder) ? remainder : KeyJoinProjection.Answers(target, property.Name) ? property.Name : null;
            if (source is null)
            {
                Refuse($"leaves {Result.Name}.{property.Name} unread: neither {Entity.Name} nor {target.Name} has it (with the alias {alias} or without)");
                return;
            }

            // A left join reads a missing entity's fields as null, which a value type that is not nullable cannot hold.
            if (kind == 1 && property.Type.IsValueType && property.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
            {
                Refuse($"is a left join, and {Result.Name}.{property.Name} cannot hold the null a missing {target.Name} reads: make it nullable");
                return;
            }

            Joined[property.Name] = KeyJoinProjection.EffectiveName(declared, source);
        }

        JoinTarget = target;
        JoinForeignKey = foreignKey!;
        // A Pragmatic entity's Id is its shape's alias of the key (Id => PersistenceId), which the engine cannot see through on an entity of
        // another assembly (#479's AppUser): the key is named, as EF Core's step names it. Asked of the declared symbol, as EF Core asks.
        JoinTargetKey = KeyJoinProjection.EffectiveName(declared, targetKey);
        JoinDrops = kind == 0;
    }

    /// <summary>The join and the result's initializer as Sql chain operators, after the query's filtered and ordered chain; its inner chain on <paramref name="session" />.</summary>
    internal string JoinedChain(string session)
    {
        var members = FromRoot.Select(p => $"{p} = e.{p}").Concat(Joined.Select(p => $"{p.Key} = j.{p.Value}"));
        var join = JoinDrops ? "Join" : "LeftJoin";
        return $".{join}({session}.Query<global::{JoinTarget!.ToDisplayString()}>(), e => e.{JoinForeignKey}, j => j.{JoinTargetKey}, (e, j) => new {ResultName} {{ {string.Join(", ", members)} }})";
    }
}
