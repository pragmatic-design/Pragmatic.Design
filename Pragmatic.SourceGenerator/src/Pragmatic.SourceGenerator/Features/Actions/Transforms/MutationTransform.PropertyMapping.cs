using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class MutationTransform
{
    private static string? FindIdProperty(INamedTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is IPropertySymbol { IsStatic: false, Name: "Id" or "PersistenceId" } prop) return prop.Name;
        }
        return null;
    }

    /// <summary>
    ///     Whether the mutation's id property can actually address a row of the entity.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every mode but Create loads an existing row, and the invoker loads it by the mutation's
    ///         <c>Id</c>. Without one, <c>LoadEntityAsync</c> is generated as
    ///         <c>Task.FromResult&lt;TEntity?&gt;(null)</c> — it compiles, it ships, and the operation
    ///         finds nothing on every call. Nothing said so among the sixty diagnostics in this range.
    ///     </para>
    ///     <para>
    ///         The type is checked too, and for the same reason: an <c>int Id</c> against a
    ///         <c>Guid</c>-keyed entity produces a comparison that either fails to compile inside a
    ///         generated file the author cannot open, or — with a strongly-typed id that converts —
    ///         compiles and matches nothing.
    ///     </para>
    /// </remarks>
    private static MutationIdProblem CheckIdProperty(
        INamedTypeSymbol symbol, string? idPropertyName, ITypeSymbol? entityIdType)
    {
        if (idPropertyName is null)
            return MutationIdProblem.Missing;

        if (entityIdType is null)
            return MutationIdProblem.None;

        var property = symbol.GetMembers(idPropertyName).OfType<IPropertySymbol>().FirstOrDefault();
        if (property is null)
            return MutationIdProblem.Missing;

        return SymbolEqualityComparer.Default.Equals(property.Type, entityIdType)
            ? MutationIdProblem.None
            : MutationIdProblem.WrongType;
    }

    private static (ImmutableArray<MutationPropertyMapModel> Mapped, ImmutableArray<UnmappedMutationPropertyModel> Unmapped, ImmutableArray<MutationChildModel> Children, ImmutableArray<MutationLinkModel> Links, ImmutableArray<string> Retargeted)
        MatchProperties(
            INamedTypeSymbol mutationType,
            INamedTypeSymbol entityType,
            Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<MutationPropertyMapModel>();
        var unmappedBuilder = ImmutableArray.CreateBuilder<UnmappedMutationPropertyModel>();
        var childBuilder = ImmutableArray.CreateBuilder<MutationChildModel>();
        var linkBuilder = ImmutableArray.CreateBuilder<MutationLinkModel>();
        var retargetedBuilder = ImmutableArray.CreateBuilder<string>();

        var entityProperties = new Dictionary<string, IPropertySymbol>(StringComparer.OrdinalIgnoreCase);
        var current = entityType;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { IsStatic: false, IsIndexer: false } prop &&
                    !entityProperties.ContainsKey(prop.Name))
                    entityProperties[prop.Name] = prop;
            }
            current = current.BaseType;
        }

        // What the generators will add — trait members and the foreign keys of [Relation.*]. They are
        // not on the symbol during this pass, and without them a mutation property named after a
        // foreign key was reported as having no setter (PRAG0414) while Set{Fk} was being generated
        // three files away.
        var generated = TraitPropertyResolver.GetGeneratedProperties(entityType);

        foreach (var member in mutationType.GetMembers())
        {
            if (member is not IPropertySymbol mutProp || mutProp.IsStatic || mutProp.IsIndexer)
                continue;
            if (mutProp.Name is "Id" or "PersistenceId")
                continue;
            // A mutation only ever writes, so an ignore that covers writing excludes it.
            if (Mapping.Analysis.AttributeAnalyzer.IsIgnoredFor(mutProp, writing: true))
                continue;

            // [MapProperty(Target = …)] names its own destination, so the by-name comparison below is
            // asking the wrong question: it reported PRAG0414 — "no setter Set{Name}" — for a write
            // that Mapping performs correctly through the declared target. Same exit as [MapIgnore]
            // and [LinkIds], for the same reason: the property is not unmatched, it is matched
            // elsewhere. Whether anyone reads the target is PRAG0445's question, not this loop's.
            if (Mapping.Analysis.AttributeAnalyzer.GetMapPropertyAttribute(mutProp) is { Target: not null })
            {
                retargetedBuilder.Add(mutProp.Name);
                continue;
            }

            // [LinkIds]: keys, not a value and not a child. It leaves the loop before the matching
            // below, which would pair a List<Guid> named LabelIds with nothing and report PRAG0414.
            if (Mapping.Analysis.LinkIdsAnalyzer.Read(mutProp) is { } link)
            {
                var linkTarget = LinkTarget(entityProperties, generated, link.Navigation);
                if (linkTarget is not null)
                    linkBuilder.Add(new MutationLinkModel
                    {
                        PropertyName = mutProp.Name,
                        Navigation = link.Navigation,
                        Key = link.Key,
                        Strategy = link.Strategy,
                        RelatedEntityFullTypeName = linkTarget,
                    });

                continue;
            }

            entityProperties.TryGetValue(mutProp.Name, out var navigation);

            // The navigation is usually one the persistence generator writes — a relation is declared
            // once, on one side, and both sides get their member from it. Looking only at declared
            // properties found nothing, so the child was skipped: the operation compiled, answered 200,
            // and wrote none of its children. Measured on a consumer, where the whole set came back
            // unchanged after a curator had settled it.
            var target = navigation is not null
                ? new MutationChildAnalyzer.EntityNavigation(
                    navigation.Name,
                    CollectionElementOf(navigation.Type) ?? navigation.Type,
                    navigation.SetMethod is { } set && set.DeclaredAccessibility != Accessibility.Public)
                : GeneratedNavigation(generated, mutProp.Name);

            // A child of the aggregate is not a value to assign: it is merged, and it leaves this loop
            // before the setter matching below could pair a List<LineDto> with an ICollection<Line>.
            if (MutationChildAnalyzer.Analyze(mutProp, target, entityType) is { } child)
            {
                childBuilder.Add(child);
                continue;
            }

            if (!entityProperties.TryGetValue(mutProp.Name, out var entityProp) ||
                entityProp.SetMethod is null)
            {
                // A generated member is assigned through its generated Set{Name}, so it maps.
                var willBeGenerated = generated.FirstOrDefault(
                    v => string.Equals(v.Name, mutProp.Name, StringComparison.OrdinalIgnoreCase));

                if (willBeGenerated.Name is null)
                {
                    // A property the invoker writes is not an input: it is written to the entity when
                    // the entity has a member of that name — [FromCurrentUser] EmployeeId on a create —
                    // and is otherwise the body's to read, as the clock's Now is for a decision. Nor is
                    // an input of a row the operation preloads: the body reads the row it finds.
                    if (InvokerBinding.IsBound(mutProp) || LoadKey.IsNamedByALoad(mutProp, compilation))
                        continue;

                    unmappedBuilder.Add(new UnmappedMutationPropertyModel
                    {
                        PropertyName = mutProp.Name,
                        EntityTypeName = entityType.Name
                    });
                    continue;
                }

                builder.Add(new MutationPropertyMapModel
                {
                    MutationPropertyName = mutProp.Name,
                    MutationPropertyTypeName = mutProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    EntityPropertyName = willBeGenerated.Name,
                    IsNullable = mutProp.Type.NullableAnnotation == NullableAnnotation.Annotated
                });
                continue;
            }

            var (canConvertCheck, expectedShape) = ConversionGuardFor(mutProp, entityProp);

            builder.Add(new MutationPropertyMapModel
            {
                MutationPropertyName = mutProp.Name,
                MutationPropertyTypeName = mutProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                EntityPropertyName = entityProp.Name,
                IsNullable = mutProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
                // A public setter gets no generated Set{Name}: the wrapper exists to reach the ones
                // the outside cannot. Calling it anyway named a method that is never written.
                EntityHasPublicSetter = entityProp.SetMethod is
                    { DeclaredAccessibility: Accessibility.Public },
                CanConvertCheck = canConvertCheck,
                ExpectedShape = expectedShape,
            });
        }

        return (builder.ToImmutable(), unmappedBuilder.ToImmutable(), childBuilder.ToImmutable(),
            linkBuilder.ToImmutable(), retargetedBuilder.ToImmutable());
    }

    /// <summary>
    ///     The check that has to pass before the write path converts this property, if any.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The write path converts a <c>string</c> into whatever the entity declares by calling
    ///         <c>Parse</c>, and <c>Parse</c> throws on a value it cannot read. Nothing catches it, so
    ///         the caller was answered 500 for a body they sent. The conversion is known here, at
    ///         compile time; so is the check that precedes it.
    ///     </para>
    ///     <para>
    ///         ⚠️ This is the only place holding both types. The template sees the mutation's
    ///         property type and the entity's property <em>name</em> — by then the entity's type is
    ///         gone, and the conversion could not be worked out at all.
    ///     </para>
    /// </remarks>
    private static (string? Check, string? Shape) ConversionGuardFor(
        IPropertySymbol mutationProperty,
        IPropertySymbol entityProperty)
    {
        var kind = Features.Mapping.Analysis.TypeConversionHelper.DetectConversion(
            mutationProperty.Type.ToDisplayString(),
            entityProperty.Type.ToDisplayString(),
            Features.Mapping.Analysis.TypeConversionHelper.IsEnumType(mutationProperty.Type),
            Features.Mapping.Analysis.TypeConversionHelper.IsEnumType(entityProperty.Type));

        if (!Features.Mapping.Analysis.TypeConversionHelper.CanFailOnValue(kind))
            return (null, null);

        // A declared [MapEnum(OnUnknown = …)] has already answered this question, and the guard runs
        // first: refusing the string here made the mapping's own `: default` branch unreachable from
        // any request — a member of a public enum nothing could execute. Left at Throw, the guard
        // stays and is the improvement it was written to be: 422 for a wrong body, not a 500.
        if (Features.Mapping.Analysis.AttributeAnalyzer.ReadEnumOnUnknown(mutationProperty) != "Throw")
            return (null, null);

        var targetType = entityProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return (
            Features.Mapping.Analysis.TypeConversionHelper.GenerateCanConvertCheck(
                kind, targetType, $"this.{mutationProperty.Name}"),
            Features.Mapping.Analysis.TypeConversionHelper.DescribeExpectedShape(kind, targetType));
    }

    /// <summary>
    ///     The entity a navigation holds — declared on the type, or predicted from its relations.
    /// </summary>
    /// <remarks>
    ///     Null when neither knows the name: a <c>[LinkIds]</c> naming a navigation that does not
    ///     exist writes nothing, rather than emitting a type argument that will not compile.
    /// </remarks>
    private static string? LinkTarget(
        Dictionary<string, IPropertySymbol> declared,
        ImmutableArray<TraitPropertyResolver.VirtualProperty> generated,
        string navigation)
    {
        ITypeSymbol? navigationType = declared.TryGetValue(navigation, out var property)
            ? property.Type
            : null;

        if (navigationType is null)
        {
            foreach (var predicted in generated)
            {
                if (!string.Equals(predicted.Name, navigation, StringComparison.Ordinal))
                    continue;

                navigationType = predicted.TypeSymbol;
                break;
            }
        }

        if (navigationType is null)
            return null;

        var element = CollectionElementOf(navigationType) ?? navigationType;
        return element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>
    ///     The navigation a generator will add, when the entity does not declare one by that name.
    /// </summary>
    private static MutationChildAnalyzer.EntityNavigation? GeneratedNavigation(
        ImmutableArray<TraitPropertyResolver.VirtualProperty> generated, string name)
    {
        foreach (var virtualProperty in generated)
        {
            if (!string.Equals(virtualProperty.Name, name, StringComparison.OrdinalIgnoreCase)
                || virtualProperty.TypeSymbol is null)
                continue;

            // ⚠️ Only a generated COLLECTION is written without assigning: the merge writes into it.
            // A generated reference navigation is `public Foo Name { get; set; } = null!;` —
            // EntityRelationsTemplate.RenderReferenceNavProperty — with no Set{Name} wrapper; only the
            // foreign key gets one. Predicting "never public" for both emitted `entity.SetX(v)` for a
            // navigation declared by [Relation], which is CS1061 inside a file the author cannot open.
            // Nothing caught it because no mutation in this repo wrote a relation-declared reference
            // until conformance grew one.
            return new MutationChildAnalyzer.EntityNavigation(
                virtualProperty.Name, virtualProperty.TypeSymbol,
                HasNonPublicSetter: CollectionElementOf(virtualProperty.TypeSymbol) is not null);
        }

        return null;
    }

    /// <summary>The element type of a collection, or null when the type is not one.</summary>
    private static ITypeSymbol? CollectionElementOf(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        return type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
            ? named.TypeArguments[0]
            : null;
    }
}
