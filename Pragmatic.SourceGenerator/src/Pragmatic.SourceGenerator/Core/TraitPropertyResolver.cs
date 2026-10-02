using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Resolves "virtual" properties that EntityTraitsTemplate will generate for an entity.
///     Other SG features (Mapping, Endpoints) use this to consider trait-generated properties
///     when resolving property matches, since generated properties are not yet visible on the
///     INamedTypeSymbol during the same generator pass.
/// </summary>
internal static class TraitPropertyResolver
{
    /// <summary>
    ///     Represents a property that a generator will add to an entity.
    /// </summary>
    /// <param name="Name">The property name that will exist once generation has run.</param>
    /// <param name="TypeFullName">Its type, as written in source.</param>
    /// <param name="TypeSymbol">
    ///     For a navigation, the entity on the other side — the element type when
    ///     <paramref name="IsCollection" /> is set. A caller that has to keep walking a property path
    ///     needs the symbol, not the name: without it a virtual property is terminal, which is why
    ///     <c>WorkItem.Description</c> could not resolve even once <c>WorkItem</c> was known.
    /// </param>
    /// <param name="IsCollection">Whether the navigation is a collection of <paramref name="TypeSymbol" />.</param>
    /// <remarks>
    ///     This carries an <see cref="ITypeSymbol" />, so it must never be stored in a model that
    ///     flows through the incremental pipeline — it is an analysis value, computed from live
    ///     symbols and consumed in the same pass.
    /// </remarks>
    internal readonly record struct VirtualProperty(
        string Name,
        string TypeFullName,
        ITypeSymbol? TypeSymbol = null,
        bool IsCollection = false);

    /// <summary>
    ///     Returns the list of properties that EntityTraitsTemplate will generate for the given entity.
    ///     Only returns properties that are NOT already declared on the type (manual declarations take precedence).
    /// </summary>
    public static ImmutableArray<VirtualProperty> GetTraitProperties(INamedTypeSymbol entitySymbol)
    {
        // Check for [Entity] — trait generation only applies to entities
        var idType = GetEntityIdType(entitySymbol);
        if (idType is null)
            return ImmutableArray<VirtualProperty>.Empty;

        var existingMembers = CollectExistingMemberNames(entitySymbol);
        var builder = ImmutableArray.CreateBuilder<VirtualProperty>();

        // PersistenceId + Id (always generated for [Entity] if not manual)
        if (!existingMembers.Contains("PersistenceId"))
        {
            builder.Add(new VirtualProperty("PersistenceId", idType));
            if (!existingMembers.Contains("Id"))
                builder.Add(new VirtualProperty("Id", idType));
        }

        // Detect [Auditable] and [SoftDelete] attributes
        var traits = TraitDetector.Detect(entitySymbol);

        // IAuditable properties
        if (traits.IsAuditable && !HasAllMembers(existingMembers, "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"))
        {
            AddIfMissing(builder, existingMembers, "CreatedAt", "System.DateTimeOffset");
            AddIfMissing(builder, existingMembers, "CreatedBy", "string?");
            AddIfMissing(builder, existingMembers, "UpdatedAt", "System.DateTimeOffset?");
            AddIfMissing(builder, existingMembers, "UpdatedBy", "string?");
        }

        // ISoftDelete properties
        if (traits.IsSoftDelete && !HasAllMembers(existingMembers, "IsDeleted", "DeletedAt", "DeletedBy"))
        {
            AddIfMissing(builder, existingMembers, "IsDeleted", "bool");
            AddIfMissing(builder, existingMembers, "DeletedAt", "System.DateTimeOffset?");
            AddIfMissing(builder, existingMembers, "DeletedBy", "string?");
        }

        // IOwnedEntity properties
        if (traits.IsOwnedEntity)
        {
            AddIfMissing(builder, existingMembers, "OwnerId", "string");
        }

        // IScopedEntity properties
        if (traits.IsScopedEntity)
        {
            AddIfMissing(builder, existingMembers, "AccessScopes", "System.Collections.Generic.List<string>");
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Quick check: will the entity have an "Id" property (either manual or trait-generated)?
    /// </summary>
    public static bool WillHaveIdProperty(INamedTypeSymbol entitySymbol)
    {
        // Check existing members first
        var current = entitySymbol;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { Name: "Id" })
                    return true;
            }

            current = current.BaseType;
        }

        // Check if [Entity] is present (which means Id will be generated)
        return GetEntityIdType(entitySymbol) is not null;
    }

    /// <summary>
    ///     Whether the entity has a generated parameterless <c>Create()</c> a caller can name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It is a question about the <b>attribute the author wrote</b>, not about the factory's
    ///         shape: <c>EntityCreateTemplate</c> emits a parameterless <c>Create()</c> for every
    ///         entity, so having one follows from being an entity.
    ///     </para>
    ///     <para>
    ///         ⚠️ With one exception this does not model: an <b>abstract</b> entity gets no factory at
    ///         all (<c>EntityCreateTemplate.Validate</c> refuses it — a derived type creates), and this
    ///         still answers yes. Latent rather than live: no abstract entity exists in the examples,
    ///         and both branches of the caller — <c>Create()</c> and <c>new</c> — fail to compile on an
    ///         abstract type, so the case wants a diagnostic and not a better prediction.
    ///     </para>
    ///     <para>
    ///         ⚠️ It does not walk the properties to reproduce the "required at creation" rule from
    ///         <c>EntityTransform</c>: that would be a second copy of one rule, kept in step only by a
    ///         test. The overload removes the thing to predict; what is left is a fact
    ///         every generator can see.
    ///     </para>
    /// </remarks>
    public static bool WillHaveParameterlessFactory(INamedTypeSymbol entitySymbol)
        => GetEntityIdType(entitySymbol) is not null;

    /// <summary>
    ///     Every property the generators will add to this entity: the trait members **and** the
    ///     foreign keys its <c>[Relation.*]</c> attributes produce.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Ask this, not <see cref="GetTraitProperties" />, whenever the question is "will this name
    ///         exist on the entity once generation has run". Mapping asked only about traits, so a DTO
    ///         property named after a relation's foreign key matched nothing: PRAG0303, and the column
    ///         dropped from the projection without an error — the client received an empty Guid.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>And the bases are asked too.</b> A declaration lives on one type —
    ///         <c>symbol.GetAttributes()</c> is scoped to it, whatever an attribute's <c>Inherited</c>
    ///         says — so a <c>[Relation]</c> or a trait declared on the base of a
    ///         <c>[Inheritance(Tph)]</c> hierarchy produced members that were invisible from the
    ///         derived type. A DTO over the derived entity then reported the base's own foreign key as
    ///         missing: PRAG0303 on <c>InvoiceId</c>, PRAG0302 on a path through the base's navigation,
    ///         with nothing wrong in the source. Properties declared by hand never had this
    ///         problem, because <c>PropertyAnalyzer.GetAllProperties</c> has always walked bases —
    ///         which is why only the generated half was broken, and why it took a TPH example to find.
    ///     </para>
    /// </remarks>
    public static ImmutableArray<VirtualProperty> GetGeneratedProperties(INamedTypeSymbol entitySymbol)
    {
        var own = GeneratedPropertiesOf(entitySymbol);
        if (entitySymbol.BaseType is not { } baseType || GetEntityIdType(baseType) is null)
            return own;

        // A derived entity has everything its base has. Its own declaration wins on a name collision,
        // which is the C# rule for a member that hides one.
        var builder = ImmutableArray.CreateBuilder<VirtualProperty>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = entitySymbol; current is not null; current = current.BaseType)
        {
            if (!ReferenceEquals(current, entitySymbol) && GetEntityIdType(current) is null)
                break;

            foreach (var property in ReferenceEquals(current, entitySymbol) ? own : GeneratedPropertiesOf(current))
                if (seen.Add(property.Name))
                    builder.Add(property);
        }

        return builder.ToImmutable();
    }

    /// <summary>The members generation adds because of what <b>this one type</b> declares.</summary>
    private static ImmutableArray<VirtualProperty> GeneratedPropertiesOf(INamedTypeSymbol entitySymbol)
    {
        var traits = GetTraitProperties(entitySymbol);
        var foreignKeys = GetRelationForeignKeys(entitySymbol);
        var navigations = GetRelationNavigations(entitySymbol);
        var inverse = GetInverseRelationNavigations(entitySymbol);

        if (foreignKeys.IsEmpty && navigations.IsEmpty && inverse.IsEmpty)
            return traits;

        var builder = ImmutableArray.CreateBuilder<VirtualProperty>(
            traits.Length + foreignKeys.Length + navigations.Length + inverse.Length);
        builder.AddRange(traits);
        builder.AddRange(foreignKeys);
        builder.AddRange(navigations);
        builder.AddRange(inverse);
        return builder.ToImmutable();
    }

    /// <summary>
    ///     The navigations this entity receives because <b>another</b> entity declared the relation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A relation is declared once, on one side, and produces members on both:
    ///         <c>[Relation.OneToMany&lt;Member&gt;]</c> on <c>Workspace</c> gives Workspace its
    ///         <c>Members</c> collection <b>and</b> Member its <c>Workspace</c> back-reference. Reading
    ///         only the entity's own attributes found the first and never the second, so a DTO over the
    ///         child could not flatten through it — <c>PRAG0302, property not found</c> — and the whole
    ///         auto-include was unreachable from an application that declares relations that way.
    ///     </para>
    ///     <para>
    ///         Measured on a consumer rather than supposed: it declares every relation on the
    ///         parent and has zero hand-written navigations, so no DTO of its could reach through one.
    ///         The reference application only worked because a single entity keeps its navigation in
    ///         source, with a comment explaining that it is there for the other generator to see.
    ///     </para>
    ///     <para>
    ///         The naming is <c>RelationGraphBuilder</c>'s: the child's back-reference is
    ///         <c>Inverse</c> when given and the declaring type's name otherwise, and a relation across
    ///         a boundary yields no navigation on either side.
    ///     </para>
    /// </remarks>
    public static ImmutableArray<VirtualProperty> GetInverseRelationNavigations(INamedTypeSymbol entitySymbol)
    {
        if (GetEntityIdType(entitySymbol) is null)
            return ImmutableArray<VirtualProperty>.Empty;

        var existing = CollectExistingMemberNames(entitySymbol);
        var ownBoundary = FindBoundary(entitySymbol);
        var builder = ImmutableArray.CreateBuilder<VirtualProperty>();

        foreach (var declaring in EntitiesOf(entitySymbol.ContainingAssembly))
        {
            if (SymbolEqualityComparer.Default.Equals(declaring, entitySymbol))
                continue;

            foreach (var attr in declaring.GetAttributes())
            {
                var (kind, target) = RelationAttributeReader.Read(attr.AttributeClass);
                if (target is null || !SymbolEqualityComparer.Default.Equals(target, entitySymbol))
                    continue;

                // Only the side that owns a navigation produces one on the other side too.
                var declaresCollection = RelationAttributeReader.OwnsCollectionNavigation(kind);
                if (!declaresCollection && !RelationAttributeReader.OwnsReferenceNavigation(kind))
                    continue;

                if (IsCrossBoundary(ownBoundary, FindBoundary(declaring)) && !ReadsAcross(declaring, entitySymbol))
                    continue;

                // A collection on the declaring side puts a reference on this one, and the other way
                // round — except many-to-many, which is a collection at both ends.
                var isManyToMany = declaresCollection && RelationAttributeReader.OwnsReferenceNavigation(kind);
                var inverseIsCollection = isManyToMany || !declaresCollection;

                var inverseName = GetStringArgument(attr, "Inverse");
                var name = inverseIsCollection
                    ? RelationNavigationNaming.Collection(inverseName, declaring.Name)
                    : RelationNavigationNaming.Reference(inverseName, declaring.Name);

                if (existing.Contains(name) || builder.Any(v => v.Name == name))
                    continue;

                builder.Add(new VirtualProperty(
                    name,
                    declaring.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    declaring,
                    inverseIsCollection));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The entities of an assembly, worked out once and held only as long as its symbol lives.
    /// </summary>
    /// <remarks>
    ///     Finding an inverse means asking which other type declared the relation, and that question
    ///     has no answer on the entity's own symbol. The walk is over one assembly and the result is
    ///     keyed weakly on it, so a new compilation gets new symbols and a new answer rather than a
    ///     stale one.
    /// </remarks>
    private static IReadOnlyList<INamedTypeSymbol> EntitiesOf(IAssemblySymbol assembly)
    {
        if (EntityCache.TryGetValue(assembly, out var cached))
            return cached;

        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        Collect(assembly.GlobalNamespace, builder);
        var entities = builder.ToArray();

        EntityCache.Add(assembly, entities);
        return entities;

        static void Collect(INamespaceSymbol ns, ImmutableArray<INamedTypeSymbol>.Builder into)
        {
            foreach (var member in ns.GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol nested:
                        Collect(nested, into);
                        break;
                    case INamedTypeSymbol type when GetEntityIdType(type) is not null:
                        into.Add(type);
                        break;
                }
            }
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        IAssemblySymbol, INamedTypeSymbol[]> EntityCache = new();

    /// <summary>
    ///     The navigations the entity's <c>[Relation.*]</c> attributes will generate: the reference
    ///     one on the side that owns the foreign key, the collection one on the other.
    /// </summary>
    /// <remarks>
    ///     Names come from <see cref="RelationNavigationNaming" />, the same rule
    ///     <c>RelationGraphBuilder</c> emits from. A relation that crosses a boundary the entity's
    ///     boundary cannot read produces no navigation at all — only the foreign key — so predicting
    ///     one here would announce a member that never appears. A crossing declared with
    ///     <c>[ReadAccess&lt;T&gt;]</c> does get its navigation, read-only.
    /// </remarks>
    public static ImmutableArray<VirtualProperty> GetRelationNavigations(INamedTypeSymbol entitySymbol)
    {
        var existing = CollectExistingMemberNames(entitySymbol);
        var ownBoundary = FindBoundary(entitySymbol);
        var builder = ImmutableArray.CreateBuilder<VirtualProperty>();

        foreach (var attr in entitySymbol.GetAttributes())
        {
            var (kind, target) = RelationAttributeReader.Read(attr.AttributeClass);
            if (target is null)
                continue;

            var isCollection = RelationAttributeReader.OwnsCollectionNavigation(kind);
            if (!isCollection && !RelationAttributeReader.OwnsReferenceNavigation(kind))
                continue;

            if (IsCrossBoundary(ownBoundary, FindBoundary(target)) && !ReadsAcross(entitySymbol, target))
                continue;

            // ReadNavigationName, not GetStringArgument: WithNavigation("Lines") passes the name as a
            // CONSTRUCTOR argument, so it never appears among the named ones. Reading only those
            // predicted the default name — OrderLines — while EntityRelationsTemplate generated Lines,
            // and every consumer of this prediction was wrong for a renamed navigation: PRAG0303 on a
            // DTO that reads it, PRAG0439 on a mutation that writes it, and CS1061 in generated code
            // for one that trusted the prediction. The inverse side of the same file already read it
            // correctly, which is what two readers for one fact costs.
            var navigationName = ReadNavigationName(attr);
            var name = isCollection
                ? RelationNavigationNaming.Collection(navigationName, target.Name)
                : RelationNavigationNaming.Reference(navigationName, target.Name);

            // A hand-declared navigation wins: the generator skips it, so predicting it would
            // announce a duplicate.
            if (existing.Contains(name))
                continue;

            builder.Add(new VirtualProperty(
                name,
                target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                target,
                isCollection));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The relation that would have produced <paramref name="navigationName" /> had it not crossed
    ///     a boundary — the other entity and the two boundaries — or <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Asked when a mapping fails to resolve a path. The property genuinely does not exist, and
    ///         "not found on this type" is a true sentence that sends the reader looking for a typo:
    ///         the relation <em>is</em> declared, and the navigation was deliberately not generated
    ///         because the two entities live in different <c>DbContext</c>s and there is nothing for
    ///         EF to include.
    ///     </para>
    ///     <para>
    ///         Both directions are checked. The entity may declare the relation, or the other side may
    ///         declare it and this navigation be the way back — the same two cases
    ///         <see cref="GetGeneratedProperties" /> and <see cref="GetInverseRelationNavigations" />
    ///         split between them.
    ///     </para>
    /// </remarks>
    public static CrossBoundaryRelation? FindCrossBoundaryRelation(
        INamedTypeSymbol entitySymbol,
        string navigationName)
    {
        var ownBoundary = FindBoundary(entitySymbol);
        if (string.IsNullOrEmpty(ownBoundary))
            return null;

        // Declared here: [Relation.OneToMany<Other>] on this entity names the navigation directly.
        foreach (var attr in entitySymbol.GetAttributes())
        {
            var (kind, target) = RelationAttributeReader.Read(attr.AttributeClass);
            if (target is null)
                continue;

            var targetBoundary = FindBoundary(target);
            if (!IsCrossBoundary(ownBoundary, targetBoundary) || ReadsAcross(entitySymbol, target))
                continue;

            var declaredName = ReadNavigationName(attr);
            var isCollection = RelationAttributeReader.OwnsCollectionNavigation(kind);
            var name = isCollection
                ? RelationNavigationNaming.Collection(declaredName, target.Name)
                : RelationNavigationNaming.Reference(declaredName, target.Name);

            if (name == navigationName)
                return new CrossBoundaryRelation(target.Name, ownBoundary!, targetBoundary!);
        }

        // Declared on the other side: this navigation would have been the way back.
        foreach (var declaring in EntitiesOf(entitySymbol.ContainingAssembly))
        {
            if (SymbolEqualityComparer.Default.Equals(declaring, entitySymbol))
                continue;

            var declaringBoundary = FindBoundary(declaring);
            if (!IsCrossBoundary(ownBoundary, declaringBoundary) || ReadsAcross(declaring, entitySymbol))
                continue;

            foreach (var attr in declaring.GetAttributes())
            {
                var (kind, target) = RelationAttributeReader.Read(attr.AttributeClass);
                if (target is null || !SymbolEqualityComparer.Default.Equals(target, entitySymbol))
                    continue;

                var declaresCollection = RelationAttributeReader.OwnsCollectionNavigation(kind);
                var isManyToMany = declaresCollection && RelationAttributeReader.OwnsReferenceNavigation(kind);
                var inverseIsCollection = isManyToMany || !declaresCollection;

                var name = inverseIsCollection
                    ? RelationNavigationNaming.Collection(null, declaring.Name)
                    : RelationNavigationNaming.Reference(null, declaring.Name);

                if (name == navigationName)
                    return new CrossBoundaryRelation(declaring.Name, ownBoundary!, declaringBoundary!);
            }
        }

        return null;
    }

    /// <summary>The <c>WithNavigation("…")</c> name a relation attribute declares, if any.</summary>
    private static string? ReadNavigationName(AttributeData attr)
    {
        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key is "Navigation" or "NavigationName" && arg.Value.Value is string name)
                return name;
        }

        return attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string first
            ? first
            : null;
    }

    /// <summary>
    ///     The boundary a type declares with <c>[BelongsTo&lt;T&gt;]</c>, or <c>null</c> when it
    ///     declares none.
    /// </summary>
    private static string? FindBoundary(INamedTypeSymbol symbol)
        => BoundaryOwnershipReader.BoundaryOf(symbol).FullTypeName;

    /// <summary>
    ///     Mirrors <c>RelationGraphBuilder.IsCrossBoundary</c>: an unknown boundary on either side
    ///     counts as the same one, because the generator emits the navigation in that case too.
    /// </summary>
    /// <summary>
    ///     Whether the entity's boundary reads <paramref name="target" /> with <c>[ReadAccess&lt;T&gt;]</c>:
    ///     then the target is in this boundary's DbContext and a navigation to it is generated, so the
    ///     relation is not cross-boundary for any prediction made here. Mirrors
    ///     <c>RelationGraphBuilder.ProcessRelation</c>.
    /// </summary>
    private static bool ReadsAcross(INamedTypeSymbol entitySymbol, INamedTypeSymbol target)
        => BoundaryOwnershipReader.ReadAccessTypesOf(entitySymbol).Contains(target.ToDisplayString());

    private static bool IsCrossBoundary(string? ownBoundary, string? targetBoundary)
        => !string.IsNullOrEmpty(ownBoundary)
           && !string.IsNullOrEmpty(targetBoundary)
           && ownBoundary != targetBoundary;

    /// <summary>
    ///     The foreign keys the entity's <c>[Relation.*]</c> attributes will generate.
    /// </summary>
    /// <remarks>
    ///     Names and types come from <see cref="RelationForeignKeyNaming" />, the same rule
    ///     <c>RelationGraphBuilder</c> emits from — predicting them here with a second copy of the
    ///     rule is how the two would drift apart again.
    /// </remarks>
    public static ImmutableArray<VirtualProperty> GetRelationForeignKeys(INamedTypeSymbol entitySymbol)
    {
        var existing = CollectExistingMemberNames(entitySymbol);
        var builder = ImmutableArray.CreateBuilder<VirtualProperty>();

        foreach (var attr in entitySymbol.GetAttributes())
        {
            var (kind, target) = RelationAttributeReader.Read(attr.AttributeClass);

            // Only the side that owns the foreign key contributes one.
            if (!RelationAttributeReader.OwnsForeignKey(kind) || target is null)
                continue;

            var targetIdType = GetEntityIdType(target);
            if (targetIdType is null)
                continue;

            var fkName = RelationForeignKeyNaming.Name(
                GetStringArgument(attr, "ForeignKey"),
                ReadNavigationName(attr),
                target.Name);

            // A hand-declared foreign key wins: the generator skips it, so predicting it would
            // announce a duplicate.
            if (existing.Contains(fkName))
                continue;

            // The attribute's own name, and the one RelationDetection reads to write the key: a name
            // the attribute does not have matches nothing, and predicts every optional key required.
            var isRequired = GetBoolArgument(attr, "Required") ?? true;
            builder.Add(new VirtualProperty(fkName, RelationForeignKeyNaming.Type(targetIdType, isRequired)));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The foreign key the entity's declared relation to <paramref name="target" /> produces — name
    ///     and type — or <c>null</c> when the entity declares no key-owning relation to it.
    /// </summary>
    /// <remarks>
    ///     For the features that need the key — hierarchy, temporal relations, cascade, lookups —
    ///     instead of looking it up by name among the entity's members. A member found that way can
    ///     only be a hand-written one, since the generated key lives in a file no transform can see;
    ///     reading the declaration is what lets those features work on generated keys at all.
    /// </remarks>
    public static VirtualProperty? DeclaredForeignKeyTo(INamedTypeSymbol entitySymbol, INamedTypeSymbol target)
    {
        foreach (var attr in entitySymbol.GetAttributes())
        {
            var (kind, declaredTarget) = RelationAttributeReader.Read(attr.AttributeClass);
            if (!RelationAttributeReader.OwnsForeignKey(kind) || declaredTarget is null
                || !SymbolEqualityComparer.Default.Equals(declaredTarget, target))
                continue;

            var targetIdType = GetEntityIdType(target);
            if (targetIdType is null)
                continue;

            var fkName = RelationForeignKeyNaming.Name(
                GetStringArgument(attr, "ForeignKey"),
                ReadNavigationName(attr),
                target.Name);
            var isRequired = GetBoolArgument(attr, "Required") ?? true;
            return new VirtualProperty(fkName, RelationForeignKeyNaming.Type(targetIdType, isRequired));
        }

        return null;
    }

    private static string? GetStringArgument(AttributeData attr, string name)
    {
        foreach (var arg in attr.NamedArguments)
            if (arg.Key == name && arg.Value.Value is string value && value.Length > 0)
                return value;

        return null;
    }

    private static bool? GetBoolArgument(AttributeData attr, string name)
    {
        foreach (var arg in attr.NamedArguments)
            if (arg.Key == name && arg.Value.Value is bool value)
                return value;

        return null;
    }

    /// <summary>
    ///     The identifier type of an <c>[Entity]</c>, or null when the type is not one.
    /// </summary>
    /// <remarks>
    ///     Always <c>System.Guid</c> now that <c>[Entity]</c> carries no type argument — the answer is
    ///     fixed, the question "is this an entity at all" is not, and this method answers both.
    /// </remarks>
    private static string? GetEntityIdType(INamedTypeSymbol entitySymbol)
    {
        foreach (var attr in entitySymbol.GetAttributes())
        {
            if (attr.AttributeClass is { Name: "EntityAttribute" } attrClass &&
                attrClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity")
            {
                return "System.Guid";
            }
        }

        return null;
    }

    private static HashSet<string> CollectExistingMemberNames(INamedTypeSymbol entitySymbol)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var current = entitySymbol;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop)
                    names.Add(prop.Name);
            }

            current = current.BaseType;
        }

        return names;
    }

    private static bool HasAllMembers(HashSet<string> existingMembers, params string[] names)
    {
        foreach (var name in names)
        {
            if (!existingMembers.Contains(name))
                return false;
        }

        return true;
    }

    private static void AddIfMissing(
        ImmutableArray<VirtualProperty>.Builder builder,
        HashSet<string> existingMembers,
        string name,
        string typeFullName)
    {
        if (!existingMembers.Contains(name))
            builder.Add(new VirtualProperty(name, typeFullName));
    }
}
