using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class ActionTransform
{
    /// <summary>
    ///     The <c>[LoadEntity]</c> and <c>[LoadEntities]</c> declarations of an action or a mutation — both
    ///     load them the same way.
    /// </summary>
    internal static (ImmutableArray<LoadEntityModel> Entities, ImmutableArray<LoadEntityDiagnosticInfo> Diagnostics)
        ParseLoadEntities(INamedTypeSymbol symbol, Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<LoadEntityModel>();
        var diagnostics = ImmutableArray.CreateBuilder<LoadEntityDiagnosticInfo>();
        const string loadEntityAttrPrefix = "Pragmatic.Actions.Attributes.LoadEntityAttribute";
        const string loadEntitiesAttrPrefix = "Pragmatic.Actions.Attributes.LoadEntitiesAttribute";
        const string requireExistsAttrPrefix = "Pragmatic.Actions.Attributes.RequireExistsAttribute";

        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var originalDef = attrClass.OriginalDefinition.ToDisplayString();
            var isMany = originalDef.StartsWith(loadEntitiesAttrPrefix);
            var existsOnly = originalDef.StartsWith(requireExistsAttrPrefix);
            if (!isMany && !existsOnly && !originalDef.StartsWith(loadEntityAttrPrefix))
                continue;
            var attributeName = isMany ? "LoadEntities" : existsOnly ? "RequireExists" : "LoadEntity";
            if (attrClass.TypeArguments.Length == 0)
                continue;

            var entityType = attrClass.TypeArguments[0];
            var entityTypeFullName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var entityTypeShortName = entityType.Name;

            // A key or a rule, never both: the two say where the rows come from, and one of them would be
            // ignored in silence.
            var specificationName = LoadSpecification.NameOf(attr);
            var givenKey = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
            if ((givenKey is null) == (specificationName is null))
            {
                diagnostics.Add(new LoadEntityDiagnosticInfo
                {
                    EntityTypeName = entityTypeShortName,
                    Kind = LoadEntityDiagnosticKind.KeyOrSpecification,
                    AttributeName = attributeName,
                    Reason = givenKey is null ? "neither is given" : "both are given"
                });
                continue;
            }

            string? fieldNameOverride = null;
            string? include = null;
            string? byLogicKey = null;
            var requireAny = false;
            var requireReadPermission = false;
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg is { Key: "FieldName", Value.Value: string fn })
                    fieldNameOverride = fn;
                if (namedArg is { Key: "By", Value.Value: string by })
                    byLogicKey = by;
                if (namedArg is { Key: "Include", Value.Value: string paths })
                    include = paths;
                if (namedArg is { Key: "RequireAny", Value.Value: bool any })
                    requireAny = any;
                if (namedArg is { Key: "RequireReadPermission", Value.Value: bool read })
                    requireReadPermission = read;
            }

            var fieldStem = isMany ? StringHelper.Pluralize(entityTypeShortName) : entityTypeShortName;
            var fieldName = fieldNameOverride ??
                            $"_{char.ToLowerInvariant(fieldStem[0])}{fieldStem.Substring(1)}";
            var includes = CheckIncludes(entityType, entityTypeShortName, attributeName, include, diagnostics);
            var common = new LoadEntityModel
            {
                Includes = includes,
                CheckableInvariants = RulesTheOperationCanAnswer(entityType, includes, symbol, compilation),
                EntityTypeFullName = entityTypeFullName,
                EntityTypeShortName = entityTypeShortName,
                IdPropertyName = "",
                IdTypeFullName = "",
                IsMany = isMany,
                ExistsOnly = existsOnly,
                RequireAny = requireAny,
                RequireReadPermission = requireReadPermission,
                FieldName = fieldName,
                RepositoryFieldName = $"{fieldName}Repository",
                RepositoryTypeFullName = $"global::Pragmatic.Persistence.Repository.IReadRepository<{entityTypeFullName}>"
            };

            if (specificationName is not null)
            {
                if (byLogicKey is not null)
                {
                    diagnostics.Add(new LoadEntityDiagnosticInfo
                    {
                        EntityTypeName = entityTypeShortName,
                        Kind = LoadEntityDiagnosticKind.ByNotTheLogicKey,
                        AttributeName = attributeName,
                        Reason = $"By goes with a key property — [{attributeName}<{entityTypeShortName}>(nameof(Property), "
                                 + "By = ...)] — and a Specification reads by its own rule"
                    });
                    continue;
                }

                var rule = ReadBySpecification(attr, entityType, symbol, compilation, specificationName);
                if (rule.Problem is { } problem)
                {
                    diagnostics.Add(problem with { EntityTypeName = entityTypeShortName, AttributeName = attributeName });
                    continue;
                }

                builder.Add(common with
                {
                    SpecificationMember = rule.Member,
                    SpecificationIsInvocation = rule.IsInvocation,
                    SpecificationArguments = rule.Arguments
                });
                continue;
            }

            var idPropertyName = givenKey!;
            var idProperty = KeyProperty(symbol, idPropertyName);

            if (idProperty is null)
            {
                diagnostics.Add(new LoadEntityDiagnosticInfo
                {
                    EntityTypeName = entityTypeShortName,
                    Kind = LoadEntityDiagnosticKind.IdPropertyNotFound,
                    AttributeName = attributeName,
                    IdPropertyName = idPropertyName
                });
                continue;
            }

            if (byLogicKey is not null)
            {
                var (byKey, problem) = ReadByLogicKey(common, entityType, idProperty, byLogicKey, compilation);
                if (problem is not null)
                {
                    diagnostics.Add(problem with { AttributeName = attributeName });
                    continue;
                }

                builder.Add(byKey!);
                continue;
            }

            var idTypeFullName = idProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var keyType = EntityTypeHelpers.GetEntityKeyType(entityType, compilation);
            if (keyType is null)
            {
                diagnostics.Add(new LoadEntityDiagnosticInfo
                {
                    EntityTypeName = entityTypeShortName,
                    Kind = LoadEntityDiagnosticKind.KeyTypeNotFound,
                    AttributeName = attributeName
                });
                continue;
            }

            // A Nullable<TKey> is the optional load: the row is read when the key has a value. Its
            // underlying type is what has to match the entity's key — compared here, because the
            // generated GetByIdAsync(op.Key) was the only place a mismatch showed, as CS1503 in a file
            // the author cannot open.
            //
            // [LoadEntities] names a collection instead, whose element is the key: a list of them, not a
            // nullable one — a missing key in a list is a 404 like any other.
            var isOptional = !isMany && idProperty.Type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            };
            var suppliedKeyType = isMany
                ? CollectionElementType(idProperty.Type)
                : isOptional
                    ? ((INamedTypeSymbol)idProperty.Type).TypeArguments[0]
                    : idProperty.Type;

            if (!SymbolEqualityComparer.Default.Equals(suppliedKeyType, keyType))
            {
                var key = keyType.ToDisplayString();
                diagnostics.Add(new LoadEntityDiagnosticInfo
                {
                    EntityTypeName = entityTypeShortName,
                    Kind = LoadEntityDiagnosticKind.KeyTypeMismatch,
                    AttributeName = attributeName,
                    IdPropertyName = idPropertyName,
                    SuppliedKeyType = idProperty.Type.ToDisplayString(),
                    EntityKeyType = key,
                    KeyAdvice = isMany
                        ? $"declare it a collection of '{key}': 'IReadOnlyList<{key}>', '{key}[]', 'List<{key}>'"
                        : $"declare it '{key}', or '{key}?' to load it only when given"
                });
                continue;
            }

            builder.Add(common with
            {
                IdPropertyName = idPropertyName,
                IdTypeFullName = idTypeFullName,
                IsOptional = isOptional,
                KeyTypeFullName = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            });
        }

        return (WithoutExistsALoadProves(builder, diagnostics), diagnostics.ToImmutable());
    }

    /// <summary>
    ///     The property a key names on the operation — <c>nameof(RoomTypeId)</c> — or through its request,
    ///     <c>"Request.RoomTypeId"</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The lookup read the operation's own members only, so an operation that carries its
    ///         input in a nested request — the shape the framework recommends — could preload nothing, and
    ///         the row it needed was read twice: once by a validator to refuse an unknown key, once by the
    ///         body.
    ///     </para>
    ///     <para>
    ///         Every segment is a property, and each is read on the type the previous one leads to. The path
    ///         goes into the model as written: the generated code reaches the key with the same member
    ///         access — <c>action.Request.RoomTypeId</c> — so nothing else has to know it was nested.
    ///     </para>
    /// </remarks>
    private static IPropertySymbol? KeyProperty(INamedTypeSymbol symbol, string path)
    {
        ITypeSymbol? current = symbol;
        IPropertySymbol? property = null;

        foreach (var segment in path.Split('.'))
        {
            property = current?.GetMembers().OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.Name == segment && !p.IsStatic);

            if (property is null)
                return null;

            current = property.Type;
        }

        return property;
    }

    /// <summary>
    ///     The <c>Include</c> paths that name navigations — each checked now, against the entity and what the
    ///     generator adds to it: a path that names nothing would be an exception from EF at the first
    ///     request, and one that names a scalar an Include EF refuses — both a build error here instead.
    /// </summary>
    private static ImmutableArray<string> CheckIncludes(
        ITypeSymbol entityType, string entityTypeShortName, string attributeName, string? include,
        ImmutableArray<LoadEntityDiagnosticInfo>.Builder diagnostics)
    {
        var includes = ImmutableArray.CreateBuilder<string>();
        foreach (var path in (include ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (IncludePaths.FirstSegmentThatIsNotANavigation(entityType, path) is { } segment)
            {
                diagnostics.Add(new LoadEntityDiagnosticInfo
                {
                    EntityTypeName = entityTypeShortName,
                    Kind = LoadEntityDiagnosticKind.IncludeNotANavigation,
                    AttributeName = attributeName,
                    IncludePath = path,
                    IncludeSegment = segment
                });
                continue;
            }

            includes.Add(path);
        }

        return includes.ToImmutable();
    }

    /// <summary>
    ///     The element of a collection of keys — an array's, or the <c>T</c> of the <c>IEnumerable&lt;T&gt;</c>
    ///     the type is or implements — or null for a type that is not a collection.
    /// </summary>
    private static ITypeSymbol? CollectionElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;
        if (type.SpecialType == SpecialType.System_String || type is not INamedTypeSymbol named)
            return null;

        static bool IsEnumerableOfT(INamedTypeSymbol candidate)
            => candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T;

        if (IsEnumerableOfT(named))
            return named.TypeArguments[0];

        return named.AllInterfaces.FirstOrDefault(IsEnumerableOfT)?.TypeArguments[0];
    }

    internal static ImmutableArray<DependencyModel> MergeDependencies(
        ImmutableArray<DependencyModel> existing,
        ImmutableArray<LoadEntityModel> loadEntities)
    {
        // Dedup by FIELD NAME, not type: the generated PrepareActionAsync always calls
        // {RepositoryFieldName}.GetByIdAsync(...) and LoadEntityTemplate declares that exact field, so
        // the synthetic repo must always be injected into it — even if the action also injects a
        // same-typed repository under a different name. (Deduping by type dropped the injection and
        // left the field unassigned → CS0103/NRE for [LoadEntity] on a DomainAction.)
        var existingFields = new HashSet<string>(existing.Select(d => d.FieldName));
        var builder = existing.ToBuilder();

        foreach (var le in loadEntities)
        {
            if (!existingFields.Contains(le.RepositoryFieldName))
            {
                builder.Add(new DependencyModel
                {
                    FieldName = le.RepositoryFieldName,
                    TypeName = le.RepositoryTypeFullName,
                    IsReadOnly = true
                });
                existingFields.Add(le.RepositoryFieldName);
            }
        }

        return builder.ToImmutable();
    }
    /// <summary>
    ///     The loaded entity's <c>[Invariant]</c> rules this operation can answer: those whose body
    ///     reads nothing outside <paramref name="includes" />.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Fails <b>closed</b>, in both directions. A rule over a navigation the operation did not
    ///     include is left out, because evaluating it against an empty collection is a 422 on a request
    ///     that must succeed — voiding a partly paid invoice, for one. And a rule whose
    ///     body could not be read at all, which is every rule of an entity from another assembly, is
    ///     left out too: there "reads nothing" and "cannot tell" look the same, and only one of them is
    ///     safe to act on.
    /// </remarks>
    private static ImmutableArray<InvariantModel> RulesTheOperationCanAnswer(
        ITypeSymbol entityType, ImmutableArray<string> includes, INamedTypeSymbol operation, Compilation compilation)
    {
        if (entityType is not INamedTypeSymbol entity)
            return ImmutableArray<InvariantModel>.Empty;

        var loaded = new System.Collections.Generic.HashSet<string>(includes, System.StringComparer.Ordinal);

        // ⚠️ Not a collection expression: ImmutableArray<T> in netstandard2.0 does not support one
        // (CS9210), and the generator targets netstandard2.0.
        return MutationTransform.ParseInvariants(entity, operation.ContainingAssembly, compilation)
            .Where(rule => rule.BodyWasReadable && rule.Navigations.All(loaded.Contains))
            .ToImmutableArray();
    }
}
