using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

internal static partial class EndpointTransform
{
    private const string MapPropertyAttributeName = "Pragmatic.Mapping.Attributes.MapPropertyAttribute";

    private static (bool IsEndpoint, bool IsVoid, string? ResponseType, ImmutableArray<ErrorTypeModel> ErrorTypes)
        AnalyzeBaseType(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();

            // Check for Endpoint<T> variants
            if (baseName.StartsWith(EndpointShapes.Endpoint.MetadataPrefix))
            {
                var typeArgs = baseType.TypeArguments;
                var responseType = typeArgs.Length > 0
                    ? typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : null;

                var errorTypes = typeArgs.Length > 1
                    ? typeArgs.Skip(1).Select(t => new ErrorTypeModel
                    {
                        TypeName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        SimpleName = t.Name,
                        StatusCode = GetErrorStatusCode(t, symbol.ContainingAssembly),
                        ContradictedStatusCode = ContradictedStatusCode(t, symbol.ContainingAssembly)
                    }).ToImmutableArray()
                    : ImmutableArray<ErrorTypeModel>.Empty;

                return (true, false, responseType, errorTypes);
            }

            // Check for VoidEndpoint variants
            if (baseName.StartsWith(EndpointShapes.VoidEndpoint.MetadataPrefix))
            {
                var errorTypes = baseType.TypeArguments.Select(t => new ErrorTypeModel
                {
                    TypeName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    SimpleName = t.Name,
                    StatusCode = GetErrorStatusCode(t, symbol.ContainingAssembly),
                    ContradictedStatusCode = ContradictedStatusCode(t, symbol.ContainingAssembly)
                }).ToImmutableArray();

                return (true, true, null, errorTypes);
            }

            baseType = baseType.BaseType;
        }

        return (false, false, null, ImmutableArray<ErrorTypeModel>.Empty);
    }

    private static (bool IsStreaming, string? ItemType, ImmutableArray<ErrorTypeModel> ErrorTypes)
        AnalyzeStreamingBaseType(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();

            if (baseName.StartsWith(EndpointShapes.StreamingEndpoint.MetadataPrefix))
            {
                var typeArgs = baseType.TypeArguments;
                var itemType = typeArgs.Length > 0
                    ? typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : null;

                var errorTypes = typeArgs.Length > 1
                    ? typeArgs.Skip(1).Select(t => new ErrorTypeModel
                    {
                        TypeName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        SimpleName = t.Name,
                        StatusCode = GetErrorStatusCode(t, symbol.ContainingAssembly),
                        ContradictedStatusCode = ContradictedStatusCode(t, symbol.ContainingAssembly)
                    }).ToImmutableArray()
                    : ImmutableArray<ErrorTypeModel>.Empty;

                return (true, itemType, errorTypes);
            }

            baseType = baseType.BaseType;
        }

        return (false, null, ImmutableArray<ErrorTypeModel>.Empty);
    }

    private static (bool IsStreaming, string? ItemType) AnalyzeStreamingDomainAction(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();

            if (baseName.StartsWith(EndpointShapes.StreamingDomainAction.MetadataPrefix))
            {
                var itemType = baseType.TypeArguments.Length > 0
                    ? baseType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : null;
                return (true, itemType);
            }

            baseType = baseType.BaseType;
        }

        return (false, null);
    }

    /// <summary>
    ///     Reads the declared errors off <c>DomainAction&lt;TReturn, TError1…&gt;</c> the same way the
    ///     endpoint and mutation analysers read theirs.
    /// </summary>
    /// <remarks>
    ///     Those bases exist in six arities, each implementing <c>IProducesError&lt;…&gt;</c>. Without
    ///     this reader an action's domain errors would never reach <c>ErrorTypes</c>: no
    ///     <c>ProducesProblem</c>, nothing in OpenAPI, and nothing in the client manifest. An API built
    ///     on Pragmatic would not declare the 422s it can return, whichever base the author picked.
    /// </remarks>
    private static (bool IsDomainAction, bool IsVoidDomainAction, string? ReturnType,
        ImmutableArray<ErrorTypeModel> ErrorTypes) AnalyzeDomainAction(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();

            // VoidDomainAction<TError1…>: every argument is an error, there is no return type.
            if (baseName.StartsWith(EndpointShapes.VoidDomainAction.MetadataPrefix))
                return (true, true, null, ErrorsFrom(baseType.TypeArguments, symbol));

            if (baseName.StartsWith(EndpointShapes.DomainAction.MetadataPrefix))
            {
                if (baseType.TypeArguments.Length > 0)
                {
                    var returnType = baseType.TypeArguments[0]
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return (true, false, returnType, ErrorsFrom(baseType.TypeArguments.Skip(1), symbol));
                }

                return (true, false, null, ImmutableArray<ErrorTypeModel>.Empty);
            }

            baseType = baseType.BaseType;
        }

        return (false, false, null, ImmutableArray<ErrorTypeModel>.Empty);
    }

    /// <summary>
    ///     The name of the type this operation answers with, when the compiler never resolved it.
    /// </summary>
    /// <remarks>
    ///     An error symbol is still an <c>INamedTypeSymbol</c> and its fully-qualified display is the
    ///     bare name, so everything written about the endpoint would name a type that cannot exist — one
    ///     missing <c>using</c> arriving as a page of <c>CS0246</c> in files the author cannot edit.
    ///     Read off the base type's first argument, which is where both shapes carry it.
    /// </remarks>
    private static string? UnresolvedResponseType(INamedTypeSymbol symbol)
    {
        for (var baseType = symbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();

            var carriesAResponse = baseName.StartsWith(EndpointShapes.Endpoint.MetadataPrefix)
                                   || baseName.StartsWith(EndpointShapes.DomainAction.MetadataPrefix);
            if (!carriesAResponse)
            {
                // A void shape answers nothing, so there is no response type to be unresolved — and its
                // arguments are errors, which are read elsewhere.
                if (baseName.StartsWith(EndpointShapes.VoidEndpoint.MetadataPrefix)
                    || baseName.StartsWith(EndpointShapes.VoidDomainAction.MetadataPrefix))
                    return null;

                continue;
            }

            if (baseType.TypeArguments.Length == 0)
                return null;

            return baseType.TypeArguments[0].TypeKind == TypeKind.Error
                ? baseType.TypeArguments[0].ToDisplayString()
                : null;
        }

        return null;
    }

    private static ImmutableArray<ErrorTypeModel> ErrorsFrom(IEnumerable<ITypeSymbol> types, INamedTypeSymbol symbol)
        => types.Select(t => new ErrorTypeModel
        {
            TypeName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SimpleName = t.Name,
            StatusCode = GetErrorStatusCode(t, symbol.ContainingAssembly),
            ContradictedStatusCode = ContradictedStatusCode(t, symbol.ContainingAssembly)
        }).ToImmutableArray();

    private static (bool IsQuery, string? EntityType, string? ResultType, string? BoundaryType, bool IsPaged, bool IsSingle)
        AnalyzeQueryAttribute(INamedTypeSymbol symbol)
    {
        // Look for [Query<TEntity, TResult>] or [Query<TEntity, TFilter, TResult>] attribute
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var originalDef = attrClass.OriginalDefinition.ToDisplayString();
            if (!originalDef.StartsWith(EndpointShapes.Query.MetadataPrefix))
                continue;

            if (attrClass.TypeArguments.Length >= 2)
            {
                var entityType = attrClass.TypeArguments[0];
                var resultType = attrClass.TypeArguments.Length == 3
                    ? attrClass.TypeArguments[2] // [Query<TEntity, TFilter, TResult>]
                    : attrClass.TypeArguments[1]; // [Query<TEntity, TResult>]

                var entityFullName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var resultFullName = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // Read through the shared reader, so the shape the endpoint publishes and the shape the
                // generated invoker executes cannot come apart.
                return (true, entityFullName, resultFullName,
                    Core.QueryShapeReader.BoundaryOf(entityType),
                    Core.QueryShapeReader.IsPaged(symbol, attr),
                    Core.QueryShapeReader.IsSingle(attr));
            }
        }

        return (false, null, null, null, false, false);
    }

    private static (bool IsMutation, string? EntityType, string? BoundaryType,
        ImmutableArray<ErrorTypeModel> ErrorTypes, bool CanConflict, ITypeSymbol? ImpliedIdType)
        AnalyzeMutation(INamedTypeSymbol symbol, Compilation compilation)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();
            if (baseName.StartsWith(EndpointShapes.Mutation.MetadataPrefix))
            {
                var typeArgs = baseType.TypeArguments;

                // Position 0 = TEntity (always present)
                var entityType = typeArgs.Length > 0
                    ? typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : null;

                // Resolve the boundary from the entity's [BelongsTo<TBoundary>], same as queries — so the
                // derived CRUD permission is boundary-qualified ("catalog.amenity.create"), matching the
                // query path. Without this, mutation endpoints derived "amenity.create" (no boundary).
                var boundaryType = typeArgs.Length > 0
                    ? Core.QueryShapeReader.BoundaryOf(typeArgs[0])
                    : null;

                // Positions 1+ = TError types
                var errorTypes = typeArgs.Length > 1
                    ? typeArgs.Skip(1).Select(t => new ErrorTypeModel
                    {
                        TypeName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        SimpleName = t.Name,
                        StatusCode = GetErrorStatusCode(t, symbol.ContainingAssembly),
                        ContradictedStatusCode = ContradictedStatusCode(t, symbol.ContainingAssembly)
                    }).ToImmutableArray()
                    : ImmutableArray<ErrorTypeModel>.Empty;

                // The Id a load-mode mutation is addressed by, when it declares none: this generator
                // writes it, so it is not on the symbol and every consumer that binds or constructs the
                // mutation has to be told it is coming.
                var impliedId = typeArgs.Length > 0
                                && DetectMutationModeName(symbol) is not null and not "Create"
                                && !symbol.GetMembers("Id").OfType<IPropertySymbol>().Any()
                    ? Actions.Transforms.EntityTypeHelpers.GetEntityKeyType(typeArgs[0], compilation)
                    : null;

                return (true, entityType, boundaryType, errorTypes,
                    typeArgs.Length > 0 && EntityCanConflict(typeArgs[0]), impliedId);
            }

            baseType = baseType.BaseType;
        }

        return (false, null, null, ImmutableArray<ErrorTypeModel>.Empty, false, null);
    }

    /// <summary>
    ///     Whether the entity carries something the database can reject as a conflict.
    /// </summary>
    /// <remarks>
    ///     A <c>[LogicKey]</c> sits behind a unique index and <c>[ConcurrencyAware]</c> puts a token on
    ///     the row: either one makes 409 a status this operation can answer with, whoever wrote it.
    ///     <para>
    ///         <c>[GeneratedValue]</c> does not count here: it generates a formatted value and creates
    ///         no constraint, so counting it would make an operation on an entity carrying only that one
    ///         declare a 409 nothing can produce. Uniqueness is <c>[LogicKey]</c>, and it is a separate decision.
    ///     </para>
    /// </remarks>
    internal static bool EntityCanConflict(ITypeSymbol? entity)
    {
        if (entity is not INamedTypeSymbol named)
            return false;

        foreach (var attribute in named.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "ConcurrencyAwareAttribute" } concurrency
                && concurrency.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity")
                return true;
        }

        foreach (var member in named.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass is not
                        { Name: "LogicKeyAttribute" } key
                    || key.ContainingNamespace?.ToDisplayString() != "Pragmatic.Persistence.Entity")
                    continue;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Reads <c>[ReturnsDto&lt;T&gt;]</c> off a mutation: the shape it puts on the wire in place of
    ///     the entity.
    /// </summary>
    /// <returns>
    ///     The DTO's qualified name, and — when it cannot be built from the entity — its name again, for
    ///     PRAG0531.
    /// </returns>
    /// <remarks>
    ///     The same attribute a scaffolded operation is decorated with, read here for one written by
    ///     hand, so there is one way to say it whichever kind of mutation you are looking at.
    /// </remarks>
    private static (string? Dto, string? WithoutMapFrom) ParseReturnsDto(
        INamedTypeSymbol symbol, string? entityFullTypeName, Compilation compilation)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            // Matched on name plus namespace, never on ToDisplayString(): a generic attribute displays
            // with its type argument, so the comparison would never hold.
            if (attribute.AttributeClass is not { Name: "ReturnsDtoAttribute" } declaration
                || declaration.ContainingNamespace?.ToDisplayString() != "Pragmatic.Persistence.Entity"
                || declaration.TypeArguments.Length != 1
                || declaration.TypeArguments[0] is not INamedTypeSymbol dto)
                continue;

            var entity = entityFullTypeName?.StartsWith("global::", StringComparison.Ordinal) == true
                ? entityFullTypeName.Substring("global::".Length)
                : entityFullTypeName;

            var mapsFromEntity = false;
            foreach (var dtoAttribute in dto.GetAttributes())
            {
                if (dtoAttribute.AttributeClass is not
                        { Name: "MapFromAttribute", TypeArguments.Length: 1 } mapFrom
                    || mapFrom.ContainingNamespace?.ToDisplayString() != "Pragmatic.Mapping.Attributes")
                    continue;

                if (entity is null || mapFrom.TypeArguments[0].ToDisplayString() == entity)
                    mapsFromEntity = true;
            }

            // ⚠️ A DTO that [Resource] scaffolds carries its [MapFrom] in generated source, which this
            // transform cannot see: a generator reads the compilation it was handed, not what another
            // generator is about to add to it. Asking the symbol therefore answers "no FromEntity" for
            // a type that has one, and PRAG0531 would tell the author to add an attribute to a file they
            // do not own and cannot edit.
            //
            // ⚠️ And the name has to be rebuilt, not read. The type does not exist yet either, so the
            // symbol is an error symbol — which is still an INamedTypeSymbol, so it passes every check
            // above — and its fully-qualified display is the bare name with no namespace at all. Taken
            // verbatim, it puts an unqualified WidgetReadDto into the generated endpoint and the
            // generated code does not compile: the diagnostic goes away and the failure moves.
            if (!mapsFromEntity && ScaffoldedReadDtoOf(dto, entityFullTypeName, compilation) is { } scaffolded)
                return (scaffolded, null);

            var qualified = dto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return mapsFromEntity ? (qualified, null) : (null, dto.Name);
        }

        return (null, null);
    }

    /// <summary>
    ///     Whether this DTO is the read shape <c>[Resource]</c> generates for that entity — the one
    ///     whose <c>FromEntity</c> exists but is not on the symbol yet.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The name is the same rule <c>ResourceCrudModel.ScaffoldedReadDto</c> uses to write it,
    ///         read from the other side. Matching on the name alone would be a convention invented
    ///         here; matching on the name <em>and</em> the entity carrying <c>[Resource]</c> is the
    ///         convention that already governs, asked of the symbol that does carry the attribute.
    ///     </para>
    ///     <para>
    ///         A DTO that is neither hand-written with <c>[MapFrom]</c> nor scaffolded by a
    ///         <c>[Resource]</c> still fails, and should: it genuinely has no <c>FromEntity</c> to
    ///         call. And a name that matches nothing at all never reaches here — the compiler rejects
    ///         the type argument first.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     The navigated path of whatever <c>[ReturnsDto&lt;T&gt;]</c> names on this mutation.
    /// </summary>
    private static string? NavigatedPathOfDeclaredDto(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "ReturnsDtoAttribute" } declaration
                && declaration.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity"
                && declaration.TypeArguments.Length == 1
                && declaration.TypeArguments[0] is INamedTypeSymbol dto)
            {
                return NavigatedPathOf(dto);
            }
        }

        return null;
    }

    /// <summary>
    ///     The first property of the response DTO that reads through a navigation, or null when it
    ///     stays within the aggregate.
    /// </summary>
    /// <remarks>
    ///     Only asked of a create. On an update the generated query carries the <c>Include</c> and the
    ///     same DTO works, which is why this cannot be a property of the DTO alone — it is a property
    ///     of the pair.
    /// </remarks>
    private static string? NavigatedPathOf(INamedTypeSymbol dto)
    {
        foreach (var member in dto.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != MapPropertyAttributeName
                    || attribute.ConstructorArguments.Length == 0)
                {
                    continue;
                }

                if (attribute.ConstructorArguments[0].Value is string path && path.Contains('.'))
                    return path;
            }
        }

        return null;
    }

    private static string? ScaffoldedReadDtoOf(
        INamedTypeSymbol dto, string? entityFullTypeName, Compilation compilation)
    {
        if (entityFullTypeName is null)
            return null;

        var entityName = entityFullTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? entityFullTypeName.Substring("global::".Length)
            : entityFullTypeName;

        var lastDot = entityName.LastIndexOf('.');
        var simpleName = lastDot < 0 ? entityName : entityName.Substring(lastDot + 1);

        if (dto.Name != $"{simpleName}ReadDto")
            return null;

        var entity = compilation.GetTypeByMetadataName(entityName);
        if (entity is null)
            return null;

        foreach (var attribute in entity.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeNames.Resource)
                continue;

            // The template writes the DTO into the entity's own namespace, so that is where it will
            // be — spelled out here rather than taken from the symbol, which has no namespace to give.
            return lastDot < 0
                ? $"global::{dto.Name}"
                : $"global::{entityName.Substring(0, lastDot)}.{dto.Name}";
        }

        return null;
    }

    /// <summary>
    ///     Resolves the mutation mode name with the same rules as <c>MutationTransform.DetectMode</c>:
    ///     explicit <c>[Mutation(Mode = ...)]</c> wins, then the class-name prefix convention.
    ///     Used to derive the default success status code (Create → 201, everything else → 200).
    /// </summary>
    private static string? DetectMutationModeName(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name != "MutationAttribute")
                continue;

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg is { Key: "Mode", Value.Value: int modeValue } && modeValue != 0)
                {
                    return modeValue switch
                    {
                        1 => "Create",
                        2 => "Update",
                        3 => "CreateOrUpdate",
                        4 => "Delete",
                        8 => "Restore",
                        _ => null,
                    };
                }
            }
        }

        var name = symbol.Name;
        if (name.StartsWith("Create", StringComparison.Ordinal)) return "Create";
        if (name.StartsWith("Update", StringComparison.Ordinal)) return "Update";
        if (name.StartsWith("Delete", StringComparison.Ordinal)) return "Delete";
        if (name.StartsWith("Restore", StringComparison.Ordinal)) return "Restore";
        return null;
    }
}
