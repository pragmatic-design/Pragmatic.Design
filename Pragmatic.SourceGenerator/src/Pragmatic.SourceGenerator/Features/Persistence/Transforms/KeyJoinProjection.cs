using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Works out where each property of a joined query's result reads its value.
/// </summary>
/// <remarks>
///     <para>
///         A <c>Projection</c> is one entity in and one result out, so a query that reaches a second
///         entity by key builds the whole step itself. That step has to name a source per result
///         property, and this is what decides it.
///     </para>
///     <para>
///         The order is root first, then each join in declaration order, and within a join the
///         <b>prefixed</b> name before the bare one. Root first because <c>OrderRow.Reference</c> is
///         the order's even when the customer has a reference too; prefixed first because the prefix
///         is how an author says which side they meant — and it is what <c>Alias</c> is for, since two
///         joins to the same type would otherwise share one.
///     </para>
/// </remarks>
internal static class KeyJoinProjection
{
    /// <summary>
    ///     Resolves the result type's settable properties against the entity and the joined targets.
    /// </summary>
    /// <param name="entityType">The query's root entity.</param>
    /// <param name="resultType">The result type the step projects into.</param>
    /// <param name="joins">The key joins, in declaration order — the index is the join's identity.</param>
    /// <returns>The resolved properties, and the names neither side could answer.</returns>
    public static (ImmutableArray<JoinedResultPropertyModel> Mapped, ImmutableArray<string> Unresolved) Resolve(
        ITypeSymbol entityType,
        ITypeSymbol resultType,
        ImmutableArray<(JoinModel Model, ITypeSymbol Target)> joins)
    {
        var mapped = ImmutableArray.CreateBuilder<JoinedResultPropertyModel>();
        var unresolved = ImmutableArray.CreateBuilder<string>();

        foreach (var property in SettableProperties(resultType))
        {
            if (Answers(entityType, property.Name))
            {
                mapped.Add(new JoinedResultPropertyModel
                {
                    Name = property.Name,
                    SourceProperty = EffectiveName(entityType, property.Name),
                    JoinIndex = -1,
                    NeedsNullGuard = false,
                    TypeFullName = FullyQualified(property.Type),
                });
                continue;
            }

            var resolvedFromJoin = false;
            for (var index = 0; index < joins.Length && !resolvedFromJoin; index++)
            {
                var (join, target) = joins[index];
                var source = SourcePropertyOn(target, join, property.Name);
                if (source is null)
                    continue;

                mapped.Add(new JoinedResultPropertyModel
                {
                    Name = property.Name,
                    SourceProperty = EffectiveName(target, source),
                    JoinIndex = index,
                    NeedsNullGuard = join.KeepsUnmatchedRows,
                    TypeFullName = FullyQualified(property.Type),
                });
                resolvedFromJoin = true;
            }

            if (!resolvedFromJoin)
                unresolved.Add(property.Name);
        }

        return (mapped.ToImmutable(), unresolved.ToImmutable());
    }

    /// <summary>
    ///     The name to read on a join's target for a result property, or <c>null</c> when that target
    ///     does not answer it.
    /// </summary>
    private static string? SourcePropertyOn(ITypeSymbol target, JoinModel join, string resultProperty)
    {
        var prefix = join.EffectiveAlias;
        if (resultProperty.Length > prefix.Length
            && resultProperty.StartsWith(prefix, System.StringComparison.Ordinal))
        {
            var remainder = resultProperty.Substring(prefix.Length);
            if (Answers(target, remainder))
                return remainder;
        }

        return Answers(target, resultProperty) ? resultProperty : null;
    }

    /// <summary>Whether a type can answer a property of that name, generated key included.</summary>
    public static bool Answers(ITypeSymbol type, string name) => KeyTypeOn(type, name) is not null;

    /// <summary>
    ///     Whether a key join's target is in the model of the boundary the query reads from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         EF Core composes a join only inside one <c>DbContext</c> instance, and a host builds one
    ///         per boundary — so a target the boundary neither owns nor reads is a join it cannot make.
    ///         The runtime says so at the first request; this says it at the declaration.
    ///     </para>
    ///     <para>
    ///         ⚠️ Both halves are <b>author-written</b>, which is why this belongs here and not in the
    ///         host: <c>[BelongsTo]</c>/<c>[Owns]</c> decide ownership and <c>[ReadAccess&lt;T&gt;]</c>
    ///         on the boundary class decides the rest, and the query sits beside both. The issue that
    ///         asked for this said the host had to answer, because the <c>DbContext</c> is generated
    ///         there — true of the file, not of what decides its content.
    ///     </para>
    ///     <para>
    ///         ⚠️ Silent when either boundary cannot be resolved. An entity with no boundary at all is
    ///         a different problem with its own diagnostics, and guessing here would accuse a
    ///         declaration for somebody else's omission.
    ///     </para>
    /// </remarks>
    public static bool TargetIsReachableFrom(ITypeSymbol entity, ITypeSymbol target)
    {
        if (entity is not INamedTypeSymbol namedEntity || target is not INamedTypeSymbol namedTarget)
            return true;

        var entityBoundary = BoundaryOwnershipReader.QualifiedBoundaryOf(namedEntity);
        var targetBoundary = BoundaryOwnershipReader.QualifiedBoundaryOf(namedTarget);

        if (entityBoundary is null || targetBoundary is null)
            return true;

        if (string.Equals(entityBoundary, targetBoundary, StringComparison.Ordinal))
            return true;

        var targetName = namedTarget.ToDisplayString();
        foreach (var read in BoundaryOwnershipReader.ReadAccessTypesOf(namedEntity))
            if (string.Equals(read, targetName, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>
    ///     The member a translatable expression has to name for a property of that name.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>Id</c> on an <c>[Entity]</c> is an <b>unmapped alias</b> — <c>Id =&gt; PersistenceId</c>
    ///     — so an expression naming it is one EF Core cannot turn into SQL. Measured on the Showcase:
    ///     the generated join read <c>__t0.Id</c> and every request answered 500. The mapping
    ///     generator has spelled it <c>PersistenceId</c> in its projections all along; this is the same
    ///     rule, asked through the same reader, so the two cannot drift.
    /// </remarks>
    public static string EffectiveName(ITypeSymbol type, string name)
        => name == "Id" && type is INamedTypeSymbol named && QueryTransform.IdIsTheGeneratedAlias(named)
            ? "PersistenceId"
            : name;

    /// <summary>
    ///     The properties an object initialiser can write: what the generated step builds the result
    ///     with.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>init</c> counts and a get-only property does not. A result whose values arrive through
    ///     a constructor is not this shape at all, and reporting its parameters as unresolved would be
    ///     a false accusation — so a type with no settable property produces no mapping and no
    ///     complaint, and the query keeps its ordinary projection.
    /// </remarks>
    public static IEnumerable<IPropertySymbol> SettableProperties(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member is { IsStatic: false, IsIndexer: false, SetMethod: not null }
                    && member.DeclaredAccessibility == Accessibility.Public
                    && member.SetMethod.DeclaredAccessibility == Accessibility.Public)
                {
                    yield return member;
                }
            }
        }
    }

    /// <summary>A public instance property of that name, on the type or any of its bases.</summary>
    public static IPropertySymbol? FindProperty(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name).OfType<IPropertySymbol>())
            {
                if (member is { IsStatic: false, IsIndexer: false }
                    && member.DeclaredAccessibility == Accessibility.Public)
                {
                    return member;
                }
            }
        }

        foreach (var member in type.AllInterfaces.SelectMany(i => i.GetMembers(name)).OfType<IPropertySymbol>())
        {
            if (member is { IsStatic: false, IsIndexer: false })
                return member;
        }

        return null;
    }

    /// <summary>
    ///     The type of a join key on a type, or <c>null</c> when it names nothing there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A generator does not see its own output, so an entity's <c>Id</c>, its trait columns
    ///         and the foreign keys its <c>[Relation.*]</c> attributes produce have no symbol in the
    ///         compilation that declares them. <c>TraitPropertyResolver.GetGeneratedProperties</c> is
    ///         asked for exactly that, and it is the same reader Mapping uses — where, without it, a DTO
    ///         property named after a relation's foreign key would match nothing, and the column would
    ///         be dropped from the projection without an error.
    ///     </para>
    ///     <para>
    ///         Without this the <b>default</b> <c>TargetKey = "Id"</c> would be refused on every
    ///         entity of the module that declares it — a diagnostic firing on the case it exists to
    ///         protect.
    ///     </para>
    /// </remarks>
    public static string? KeyTypeOn(ITypeSymbol type, string name)
    {
        if (FindProperty(type, name) is { } property)
            return FullyQualified(property.Type);

        if (type is INamedTypeSymbol named)
        {
            foreach (var generated in TraitPropertyResolver.GetGeneratedProperties(named))
                if (generated.Name == name)
                    return generated.TypeSymbol is { } symbol ? FullyQualified(symbol) : generated.TypeFullName;
        }

        if (name is "Id" or "PersistenceId" && IsEntity(type))
            return "global::System.Guid";

        return null;
    }

    /// <summary>
    ///     The type as the generated file has to spell it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Fully qualified, always, including <c>global::System.String</c> for <c>string</c>. The
    ///     generated step writes <c>default(T)</c> for an outer join's missing row, and a display
    ///     string is not a type identifier: it is resolved in the generated file's own context, where
    ///     the author's usings do not exist.
    /// </remarks>
    public static string FullyQualified(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static bool IsEntity(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == EntityTransform.EntityAttributeNonGenericName))
            {
                return true;
            }
        }

        return false;
    }
}
