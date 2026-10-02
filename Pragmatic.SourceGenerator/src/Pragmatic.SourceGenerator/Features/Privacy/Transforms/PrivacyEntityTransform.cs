using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Transforms;

/// <summary>
///     Builds a <see cref="PrivacyEntityModel" /> from a type symbol.
/// </summary>
/// <remarks>
///     Reached from three separate triggers — <c>[DataSubject]</c>, <c>[LinksToSubject]</c> and
///     <c>[PersonalData]</c> — because an entity can be interesting for any of them alone. In
///     particular, one that classifies personal data but declares no path is exactly the case PRAG2900
///     exists to report, and triggering only on the class-level attributes would make it invisible.
/// </remarks>
internal static class PrivacyEntityTransform
{
    /// <summary>Builds the model for a type, or null when the symbol is not a type at all.</summary>
    public static PrivacyEntityModel? FromType(INamedTypeSymbol? type)
    {
        if (type is null)
            return null;

        var subjectPath = PrivacyAttributeReader.ReadSubjectPath(type);
        var owned = new List<string>();
        var properties = ReadProperties(type, owned);

        return new PrivacyEntityModel
        {
            FullTypeName = type.ToDisplayString(),
            TypeName = type.Name,
            Namespace = type.ContainingNamespace?.IsGlobalNamespace == false
                ? type.ContainingNamespace.ToDisplayString()
                : string.Empty,
            SubjectIdentifier = PrivacyAttributeReader.ReadSubjectIdentifier(type),
            SubjectPath = subjectPath,
            SubjectPathTargetFullTypeName = ResolvePathTarget(type, subjectPath),
            Properties = properties,
            FoldedTypeFullNames = owned.ToEquatableArray(),
            IsPersistenceEntity = IsPersistenceEntity(type),
            // Read through the persistence transform rather than re-implemented: the adapter has to ask
            // DI for the same context the entity's repository is handed, and two readings of
            // [BelongsTo] would drift into two different keys the day one of them learns a new shape.
            BoundaryTypeFullName = Persistence.Transforms.EntityTransform.GetBoundaryInfo(type).FullTypeName,
            Location = LocationInfo.From(type.Locations.FirstOrDefault())
        };
    }

    /// <summary>
    ///     Whether the type is a Pragmatic entity, and therefore gets generated <c>Set{Property}</c>
    ///     methods for its private-setter properties.
    /// </summary>
    private static bool IsPersistenceEntity(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass?.Name == "EntityAttribute" &&
                attributeClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity")
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Resolves the type a declared path leads to.
    /// </summary>
    /// <returns>
    ///     Null when the named property does not exist or is not a reference to another type. That is
    ///     PRAG2906 — reported, not silently treated as "no path", because a path that cannot be
    ///     followed and no path at all have different causes and different fixes.
    /// </returns>
    private static string? ResolvePathTarget(INamedTypeSymbol type, string? pathProperty)
    {
        if (pathProperty is null)
            return null;

        var property = type.GetMembers(pathProperty).OfType<IPropertySymbol>().FirstOrDefault();

        // A navigation resolves to its own type; a foreign key resolves to nothing here, which is why
        // the attribute is documented as naming the navigation.
        if (property?.Type is INamedTypeSymbol { TypeKind: TypeKind.Class } target)
            return NameOf(target);

        // A navigation declared with [Relation] is generated, so it is not a member during this pass —
        // and it is the form the framework recommends. Ask what the generators will add.
        var generated = property is null
            ? TraitPropertyResolver.GetGeneratedProperties(type)
                .FirstOrDefault(p => p.Name == pathProperty && !p.IsCollection)
                .TypeSymbol
            : null;

        return generated is null ? null : NameOf(generated);
    }

    /// <summary>
    ///     A type's name as this analysis compares it: without the nullable annotation.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>ToDisplayString()</c> carries the annotation, so the type of a nullable navigation
    ///     reads <c>App.Employee?</c> and matches no declared subject, and the type of an optional owned
    ///     record reads <c>App.Credentials?</c> and matches no model. Both comparisons are against a
    ///     declared type, which never carries one. Found by a control case: a link declared
    ///     <c>Employee? Manager</c> resolved to nothing and was reported as having no path at all.
    /// </remarks>
    private static string NameOf(ITypeSymbol type)
        => type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();

    /// <summary>
    ///     Every property the entity holds: its own, and those of what it owns, as paths.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The descent is the same shape the redaction map walks — <see cref="OwnedMemberWalk" />
    ///         for the depth, the cycle rule and what is worth entering — because a column masked in the
    ///         logs and absent from the processing register is two mechanisms disagreeing about what one
    ///         entity holds.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>What it does not enter.</b> A type that is an entity of its own — <c>[Entity]</c>,
    ///         <c>[DataSubject]</c> or <c>[LinksToSubject]</c> — has its own model, its own route to the
    ///         subject and its own plan: folding its columns in here would report them twice and erase
    ///         them from a plan that does not own them. Nor a collection: what is behind one is either
    ///         such an entity or a list of values, and neither the plan nor the extractor can name an
    ///         element honestly. Nor a value type, because a copy is what would be written to.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>It reads what a type inherits as well as what it declares.</b> A base is not an
    ///         entity of its own either — no row, no path to a subject — so its members are analysed
    ///         through whoever inherits them and it is folded in the same way an owned record is.
    ///     </para>
    /// </remarks>
    private static EquatableArray<ClassifiedPropertyModel> ReadProperties(
        INamedTypeSymbol type, List<string> owned)
    {
        var properties = new List<ClassifiedPropertyModel>();

        Collect(type, properties, owned, prefix: string.Empty, depth: 0,
            onThePath: new HashSet<string>(StringComparer.Ordinal) { NameOf(type) });

        return properties.ToEquatableArray();
    }

    private static void Collect(
        INamedTypeSymbol current,
        List<ClassifiedPropertyModel> into,
        List<string> owned,
        string prefix,
        int depth,
        HashSet<string> onThePath)
    {
        // Its own and its bases': a column a type inherits is a column it holds, and the ruling
        // PRAG2903 asks for is about the row, not about which file declared it.
        foreach (var baseType in OwnedMemberWalk.BasesFoldedInto(current))
        {
            var baseName = NameOf(baseType);
            if (!owned.Contains(baseName))
                owned.Add(baseName);
        }

        foreach (var member in OwnedMemberWalk.PropertiesIncludingInherited(current))
        {
            var path = prefix.Length == 0 ? member.Name : prefix + "." + member.Name;

            into.Add(new ClassifiedPropertyModel
            {
                Name = path,
                OwnerPath = prefix,
                TypeDisplay = member.Type.ToDisplayString(),
                IsReferenceType = member.Type.IsReferenceType,
                Classification = PrivacyAttributeReader.ReadPersonalData(member),
                NotPersonalReason = PrivacyAttributeReader.ReadNotPersonalReason(member),
                // init counts as unsettable: it is public, and assignable only from a constructor or an
                // object initializer — neither of which the erasure plan is.
                IsPubliclySettable = member.SetMethod is
                    { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false },
                Location = LocationInfo.From(member.Locations.FirstOrDefault())
            });

            if (depth + 1 >= OwnedMemberWalk.MaxDepth || !IsOwned(member, out var ownedType))
                continue;

            var ownedName = NameOf(ownedType);
            if (!owned.Contains(ownedName))
                owned.Add(ownedName);

            // Per branch and not global: two members of the same owned type are both walked, and a
            // record that points back at its owner ends this branch instead of the generator.
            if (!onThePath.Add(ownedName))
                continue;

            Collect(ownedType, into, owned, path, depth + 1, onThePath);
            onThePath.Remove(ownedName);
        }
    }

    /// <summary>
    ///     Whether a property is something the entity <em>owns</em> — an owned record, a value object —
    ///     rather than a value, a collection or a reference to another entity.
    /// </summary>
    private static bool IsOwned(IPropertySymbol member, out INamedTypeSymbol owned)
    {
        owned = null!;

        var (elementType, isCollection) = OwnedMemberWalk.Unwrap(member.Type);
        if (isCollection || elementType is not INamedTypeSymbol named || !named.IsReferenceType)
            return false;

        if (!OwnedMemberWalk.CanHoldDeclarations(named) || IsAnEntityOfItsOwn(named))
            return false;

        owned = named;
        return true;
    }

    /// <summary>
    ///     Whether a type is modelled in its own right, and therefore analysed in its own right.
    /// </summary>
    private static bool IsAnEntityOfItsOwn(INamedTypeSymbol type)
    {
        if (IsPersistenceEntity(type))
            return true;

        foreach (var attribute in type.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass?.ContainingNamespace?.ToDisplayString() != "Pragmatic.Privacy")
                continue;

            if (attributeClass.Name is "DataSubjectAttribute" or "LinksToSubjectAttribute")
                return true;
        }

        return false;
    }
}
