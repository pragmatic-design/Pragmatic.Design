using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

internal static partial class EndpointTransform
{
    /// <summary>
    ///     The entities a domain action reaches, read off the dependencies it declares.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A mutation and a query name their entity in their own declaration; a domain action does
    ///         not, which left it absent from the Article 30 register — and an import that writes people
    ///         is exactly the processing that register exists to record.
    ///     </para>
    ///     <para>
    ///         Two shapes, because actions come in two. One owns its data and holds an
    ///         <c>IRepository&lt;TEntity&gt;</c>; one only composes and holds an
    ///         <c>IMutationInvoker&lt;TMutation, TEntity&gt;</c>. Reading only the first would have found
    ///         nothing for <c>ImportMembersAction</c>, whose whole job is to invite members one at a time.
    ///     </para>
    ///     <para>
    ///         <b>Deliberately broader than <c>ActionTransform.ParseBelongsToFromEntity</c>, which reads
    ///         repositories only — this is not drift between two copies of one rule.</b> They answer
    ///         different questions: boundary inference asks which unit of work the action writes in, and a
    ///         composing action has none of its own (PRAG0431 says so, and tells the author to name it).
    ///         The register asks which personal data the operation reaches, and reaching it through
    ///         another operation still reaches it.
    ///     </para>
    ///     <para>
    ///         What it will not do is guess. An action that gets at data some other way — a raw command, a
    ///         service of its own — yields nothing here and stays out of the register rather than being
    ///         listed against an entity nobody derived.
    ///     </para>
    ///     <para>
    ///         What the action <b>loads</b> is inferred too (<see cref="ParseLoadedEntities" />): a load's
    ///         repository field is generated, so no dependency names its entity, and without this the
    ///         author restated it with <c>[ProcessesData&lt;T&gt;]</c> by hand.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<string> ParseDomainActionEntities(INamedTypeSymbol symbol)
    {
        var found = new List<string>(ParseInferredEntities(symbol, isDomainAction: true));
        var seen = new HashSet<string>(found, System.StringComparer.Ordinal);

        foreach (var declared in DeclaredEntities(symbol))
        {
            if (seen.Add(declared))
                found.Add(declared);
        }

        found.Sort(System.StringComparer.Ordinal);

        // Not a collection expression: ImmutableArray<T> carries no CollectionBuilder in netstandard2.0
        // (CS9210), which is the same gap EquatableArray<T> exists to fill on the model side.
        return ImmutableArray.CreateRange(found);
    }

    /// <summary>
    ///     The dependency types this compilation could not resolve — which is what composing through a
    ///     generated boundary interface looks like from inside the module that uses it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>The symbol not resolving is the signal, and it is a fact rather than a
    ///         convention.</b> The interface is written by this generator in this same pass, so the
    ///         user's compilation does not contain it: the field's type is an error symbol carrying a
    ///         name and nothing else — no attributes, no base interfaces, nothing structural to match
    ///         on. Matching the <em>name</em> instead would fire on any hand-written
    ///         <c>I…Actions</c> and miss a boundary interface named otherwise, and a false positive on
    ///         a warning is how the warning gets suppressed.
    ///     </para>
    ///     <para>
    ///         In a build that otherwise succeeds an unresolved dependency type can only be generated
    ///         code; anything else is a <c>CS0246</c> the author is already reading.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<string> ParseUnresolvedDependencies(INamedTypeSymbol symbol)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var member in symbol.GetMembers())
        {
            var type = member switch
            {
                IFieldSymbol { IsImplicitlyDeclared: false } field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };

            if (type is not { TypeKind: TypeKind.Error })
                continue;

            var name = type.Name;
            if (name.Length > 0 && seen.Add(name))
                found.Add(name);
        }

        found.Sort(System.StringComparer.Ordinal);

        return ImmutableArray.CreateRange(found);
    }

    /// <summary>Whether the operation states what personal data it reaches, in either form.</summary>
    /// <remarks>
    ///     Either <c>[ProcessesData&lt;TEntity&gt;]</c> — one or more — or the non-generic
    ///     <c>[ProcessesData]</c>, which says "reviewed, none". Both are answers; only silence is not.
    /// </remarks>
    private static bool ParseDeclaresProcessedData(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var definition = attribute.AttributeClass?.OriginalDefinition;
            if (definition is null)
                continue;

            var name = MetadataNameOf(definition);
            if (name is AttributeNames.PrivacyProcessesData or AttributeNames.PrivacyProcessesNoData)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     What the generator can see the operation reaches, without its <c>[ProcessesData&lt;T&gt;]</c>: the
    ///     dependencies of a domain action and every load — the set a declaration beside it only restates.
    /// </summary>
    private static ImmutableArray<string> ParseInferredEntities(INamedTypeSymbol symbol, bool isDomainAction)
    {
        var inferred = new HashSet<string>(ParseLoadedEntities(symbol), System.StringComparer.Ordinal);

        if (isDomainAction)
        {
            foreach (var member in symbol.GetMembers())
            {
                var type = member switch
                {
                    IFieldSymbol { IsImplicitlyDeclared: false } field => field.Type as INamedTypeSymbol,
                    IPropertySymbol property => property.Type as INamedTypeSymbol,
                    _ => null
                };

                if (EntityBehind(type) is { } entity)
                    inferred.Add(entity);
            }
        }

        var sorted = inferred.ToList();
        sorted.Sort(System.StringComparer.Ordinal);
        return ImmutableArray.CreateRange(sorted);
    }

    /// <summary>
    ///     The entities an operation's declared loads read: <c>[LoadEntity&lt;T&gt;]</c> and
    ///     <c>[LoadEntities&lt;T&gt;]</c> — by key or by rule — and, through the query, the entity of each
    ///     <c>[LoadFrom&lt;TQuery&gt;]</c>.
    /// </summary>
    /// <remarks>
    ///     A query's entity is listed against the operation too: the operation reads that data, through the
    ///     query's DTOs, and the register describes the processing each operation does. <c>[LoadCurrentUser]</c>
    ///     reads the <c>[PragmaticUser]</c> entity, which is known only to the pipeline — it is added after
    ///     this, from <see cref="Models.EndpointModel.LoadsCurrentUser" />.
    /// </remarks>
    private static IEnumerable<string> ParseLoadedEntities(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (Core.LoadKey.IsALoad(attribute)
                && attribute.AttributeClass is { TypeArguments.Length: 1 } load
                && load.TypeArguments[0] is INamedTypeSymbol entity)
                yield return entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        foreach (var attribute in property.GetAttributes())
        {
            if (!Core.InvokerBinding.IsLoadFrom(attribute) || attribute.AttributeClass!.TypeArguments[0] is not INamedTypeSymbol query)
                continue;

            foreach (var queryAttribute in query.GetAttributes())
            {
                if (queryAttribute.AttributeClass is { TypeArguments.Length: >= 1 } queryClass
                    && queryClass.OriginalDefinition.ToDisplayString().StartsWith(EndpointShapes.Query.MetadataPrefix, System.StringComparison.Ordinal)
                    && queryClass.TypeArguments[0] is INamedTypeSymbol queried)
                    yield return queried.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }
    }

    /// <summary>Whether the operation declares <c>[LoadCurrentUser]</c> — it reads the signed-in user's entity.</summary>
    private static bool ParseLoadsCurrentUser(INamedTypeSymbol symbol)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Actions.Attributes.LoadCurrentUserAttribute");

    /// <summary>The entities the operation's <c>[ProcessesData&lt;T&gt;]</c> name.</summary>
    private static ImmutableArray<string> ParseDeclaredEntities(INamedTypeSymbol symbol)
        => ImmutableArray.CreateRange(DeclaredEntities(symbol).Distinct(System.StringComparer.Ordinal));

    /// <summary>The entities the operation declares it reaches, which no dependency can name.</summary>
    /// <remarks>
    ///     ⚠️ The half a type cannot say. An action composing through <c>I{Boundary}Actions</c> reaches
    ///     whatever those operations reach, and the interface names no entity — so before this the
    ///     operation left the Article 30 register while going on processing the data, and nothing said
    ///     so. Read from <c>[ProcessesData&lt;TEntity&gt;]</c>, which is a statement rather than an
    ///     inference: reading the boundary's own operations would over-report, and walking the call
    ///     sites would still miss an action that reaches data some other way.
    /// </remarks>
    private static IEnumerable<string> DeclaredEntities(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var definition = attribute.AttributeClass?.OriginalDefinition;
            if (definition is null)
                continue;

            if (MetadataNameOf(definition) != AttributeNames.PrivacyProcessesData
                || definition.TypeArguments.Length != 1)
                continue;

            if (attribute.AttributeClass!.TypeArguments[0] is INamedTypeSymbol entity)
                yield return entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
    }

    /// <summary>The name as metadata spells it, which is how <c>AttributeNames</c> spells it too.</summary>
    /// <remarks>
    ///     ⚠️ Not <c>ToDisplayString()</c>. That renders a generic attribute as
    ///     <c>BoundaryActionsAttribute&lt;TBoundary&gt;</c>, while the constants carry the metadata form
    ///     <c>BoundaryActionsAttribute`1</c> — the one <c>ForAttributeWithMetadataName</c> takes. The
    ///     comparison silently never matched, and a generic attribute read this way is simply never
    ///     found.
    /// </remarks>
    private static string MetadataNameOf(INamedTypeSymbol definition)
    {
        var ns = definition.ContainingNamespace?.ToDisplayString();

        return string.IsNullOrEmpty(ns) ? definition.MetadataName : ns + "." + definition.MetadataName;
    }

    /// <summary>The entity a dependency gives access to, or null when it gives access to none.</summary>
    /// <remarks>
    ///     The type argument's position differs: an <c>IRepository</c> names the entity first, an invoker
    ///     names the operation first and the entity second. Matching on the interface name rather than on
    ///     arity keeps that explicit — an invoker with the entity in slot 0 would be a different type.
    /// </remarks>
    private static string? EntityBehind(INamedTypeSymbol? type)
    {
        if (type is null)
            return null;

        var definition = type.OriginalDefinition;
        var ns = definition.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        var index = definition.Name switch
        {
            "IRepository" when ns.StartsWith("Pragmatic.Persistence", System.StringComparison.Ordinal) => 0,
            // A read reaches personal data as surely as a write: the register records processing, and
            // reading is processing (Art. 4(2)).
            "IReadRepository" when ns.StartsWith("Pragmatic.Persistence", System.StringComparison.Ordinal) => 0,
            "IMutationInvoker" when ns.StartsWith("Pragmatic.Actions", System.StringComparison.Ordinal) => 1,
            _ => -1
        };

        if (index < 0 || type.TypeArguments.Length <= index)
            return null;

        return type.TypeArguments[index] is INamedTypeSymbol entity
            ? entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;
    }
}
