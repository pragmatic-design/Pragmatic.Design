using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;
using Pragmatic.Testing.SourceGenerator.Synthesis;

namespace Pragmatic.Testing.SourceGenerator.Transforms;

/// <summary>
///     Builds a CRUD create model from a POST endpoint (#7, phase 2): the request body fields are the
///     endpoint's writable public properties (excluding route/query/header-bound ones), each filled by the data
///     synthesizer. Only POST endpoints become create tests.
/// </summary>
internal static class CrudCreateExtractor
{
    /// <summary>
    ///     The create this endpoint is, or the reason it is not one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ No <see langword="null" /> here is silent: a
    ///     generated suite reports what it emitted and never what it declined, so an application reading it
    ///     cannot tell "no create contract" from "no such operation". The reason travels out with the
    ///     absence, and ends up in the generated coverage report.
    /// </remarks>
    public static (CrudCreateModel? Create, string? NotACreateBecause) Extract(
        INamedTypeSymbol endpointType, EndpointContractModel endpoint)
    {
        if (!string.Equals(endpoint.HttpMethod, "Post", System.StringComparison.OrdinalIgnoreCase))
            return (null, $"a create is a POST and this is a {endpoint.HttpMethod.ToUpperInvariant()}");

        // A collection-create POSTs to a route with no {param}. A POST whose route carries a {param}
        // (e.g. /properties/{id}/restore) acts on an existing resource — a sub-action/transition, not a create;
        // a synthesized body + random id would assert against a 404/400, not a real create.
        if (endpoint.Route.Contains("{"))
            return (null,
                "the route carries a parameter, so this POST acts on a resource that already exists: a "
                + "synthesised body against a random id would assert a 404 and not a create");

        // Multipart/file-upload endpoints don't take a JSON body; a JSON {} yields 415, not the create/400
        // contract. Skip them (their auth contract is still covered by the auth tests).
        if (endpointType.GetMembers().OfType<IPropertySymbol>().Any(IsFormBound))
            return (null, "it takes a multipart body, and a JSON one answers 415 rather than the create contract");

        // The round-trip "POST body -> 2xx" contract only holds for a simple create — a Mutation<TEntity>. A
        // DomainAction (a command: open a location, build a dashboard) can have arbitrary business preconditions
        // a synthesized body won't meet, so its success is not a generatable contract; its auth is still covered.
        //
        // It is still recorded, flagged, because a state-transition test has to arrange the entity somehow, and
        // an entity with a state machine is typically created by a command and never by a plain CRUD mutation.
        // CrudContractTestTemplate skips these; StateTransitionExtractor accepts them.
        var entity = MutationEntity(endpointType);
        var isDomainActionCreate = entity is null;
        if (isDomainActionCreate)
        {
            entity = CommandCreatedEntity(endpointType);
            if (entity is null)
                return (null, "no entity could be resolved for it: it is neither a Mutation<TEntity> nor a command whose name or repository names one");
        }

        // The same reading a transition's body gets, from the same place: which members travel in the
        // body, and a value the application will accept for each.
        var (fields, canSynthesizeBody) = RequestBodyReader.Read(endpointType, entity, endpoint.Route);

        if (fields.Length == 0)
            return (null, "nothing travels in its body, so there is no create request to build");

        return (new CrudCreateModel
        {
            Boundary = endpoint.Boundary,
            OperationName = endpoint.ActionName,
            Route = endpoint.Route,
            Permission = endpoint.Permission,
            Fields = new EquatableArray<CrudFieldModel>(fields),
            IsTenantScoped = entity!.AllInterfaces.Any(i => i.Name == "ITenantEntity"),
            EntityTypeName = entity.Name,
            CanSynthesizeBody = canSynthesizeBody,
            IsDomainActionCreate = isDomainActionCreate
        }, null);
    }

    /// <summary>
    ///     Whether omitting this property would make the request invalid: a C# <c>required</c> member, or a
    ///     non-nullable value type / non-nullable reference the endpoint declares it needs.
    /// </summary>
    internal static bool IsRequiredField(IPropertySymbol property)
    {
        if (property.IsRequired)
            return true;

        return property.Type.NullableAnnotation != NullableAnnotation.Annotated
               && property.Type is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    /// <summary>
    ///     The entity a command creates, for a <c>DomainAction</c> that has no <c>Mutation&lt;TEntity&gt;</c>
    ///     to read it from.
    ///     <para>
    ///         Resolved through the endpoint's repository field where visible, and otherwise by name: a
    ///         <c>CreateInvoiceWithFeesAction</c> creates an <c>Invoice</c>. The name route matters because the
    ///         repository field is private, and private fields are absent from the reference assemblies a test
    ///         project compiles against — the symbol lookup returns nothing in exactly the layout that is used
    ///         in practice.
    ///     </para>
    /// </summary>
    private static INamedTypeSymbol? CommandCreatedEntity(INamedTypeSymbol endpointType)
    {
        if (RepositoryEntity(endpointType) is { } fromRepository)
            return fromRepository;

        // "CreateInvoiceWithFeesAction" → look for a type whose name the action name contains.
        var actionName = endpointType.Name;
        if (!actionName.StartsWith("Create", System.StringComparison.Ordinal))
            return null;

        INamedTypeSymbol? best = null;
        foreach (var candidate in EnumerateTypes(endpointType.ContainingAssembly.GlobalNamespace))
        {
            if (candidate.TypeKind != TypeKind.Class || candidate.Name.Length < 3)
                continue;
            // A name contains itself, and the command is the longest such match — so every command whose
            // repository field was invisible resolved to itself, and no transition could ever correlate to
            // it. Silent in a single compilation, where the private field is still there.
            if (SymbolEqualityComparer.Default.Equals(candidate, endpointType))
                continue;
            if (!actionName.Contains(candidate.Name))
                continue;
            // Prefer the longest match: "Invoice" beats "In" inside CreateInvoiceWithFeesAction.
            if (best is null || candidate.Name.Length > best.Name.Length)
                best = candidate;
        }

        return best;
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
            yield return type;

        foreach (var nested in ns.GetNamespaceMembers())
            foreach (var type in EnumerateTypes(nested))
                yield return type;
    }

    /// <summary>The entity type behind the endpoint's <c>IRepository&lt;TEntity, …&gt;</c> field, if any.</summary>
    internal static INamedTypeSymbol? RepositoryEntity(INamedTypeSymbol endpointType)
    {
        foreach (var field in endpointType.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.Type is not INamedTypeSymbol { IsGenericType: true } repo)
                continue;
            if (repo.Name is not ("IRepository" or "IReadRepository") || repo.TypeArguments.Length < 1)
                continue;
            if (repo.TypeArguments[0] is INamedTypeSymbol entity)
                return entity;
        }

        return null;
    }

    /// <summary>
    ///     A format-correct value for a member carrying a validation attribute ([Email], [Phone], [Url]) — a
    ///     generic <c>"test-{guid}"</c> string would fail that validator with a 400. Returns null when no
    ///     format attribute applies, letting the type-based synthesizer take over.
    /// </summary>
    internal static string? FormatAwareValue(IPropertySymbol property, INamedTypeSymbol? entity)
    {
        // The validator may sit on the request property or on the matching entity property (e.g. a
        // Mutation<Guest> whose Guest.Email carries [Email]); consult both.
        var attributes = property.GetAttributes().AsEnumerable();
        var entityProperty = entity?.GetMembers(property.Name).OfType<IPropertySymbol>().FirstOrDefault();
        if (entityProperty is not null)
            attributes = attributes.Concat(entityProperty.GetAttributes());
        var attrList = attributes.ToList();

        foreach (var attr in attrList)
        {
            switch (attr.AttributeClass?.Name)
            {
                case "EmailAttribute":
                case "EmailAddressAttribute":
                    return "\"test-\" + global::System.Guid.NewGuid().ToString(\"N\") + \"@example.com\"";
                case "PhoneAttribute":
                    return "\"+15555550123\"";
                case "UrlAttribute":
                    return "\"https://example.com/\" + global::System.Guid.NewGuid().ToString(\"N\")";
            }
        }

        // Honour string-length constraints. The type-only synthesizer emits a long "test-"+Guid (~41 chars) that
        // overflows a tight [StringLength]/[MaxLength] (e.g. a 2-char ISO country code) → the create is rejected 400.
        if (property.Type.SpecialType == SpecialType.System_String)
        {
            int? maxLen = null, minLen = null;
            foreach (var attr in attrList)
            {
                switch (attr.AttributeClass?.Name)
                {
                    case "StringLengthAttribute":
                        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int slMax) maxLen = slMax;
                        foreach (var na in attr.NamedArguments)
                            if (na.Key == "MinimumLength" && na.Value.Value is int slMin) minLen = slMin;
                        break;
                    case "MaxLengthAttribute":
                        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int mx) maxLen = mx;
                        break;
                    case "MinLengthAttribute":
                        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int mn) minLen = mn;
                        break;
                }
            }

            if (maxLen is not null || minLen is not null)
            {
                var lo = minLen ?? 0;
                var hi = maxLen ?? int.MaxValue;
                // Prefer a unique-ish 12-char value, but stay within [lo, hi]; cap at 64 (two GUIDs of hex).
                var len = lo > 12 ? lo : (hi < 12 ? hi : 12);
                if (len > 64) len = 64;
                if (len < 1) len = 1;
                return $"(global::System.Guid.NewGuid().ToString(\"N\") + global::System.Guid.NewGuid().ToString(\"N\")).Substring(0, {len})";
            }
        }

        return null;
    }

    /// <summary>The entity behind a <c>Mutation&lt;TEntity&gt;</c> base type, if any.</summary>
    private static INamedTypeSymbol? MutationEntity(INamedTypeSymbol endpointType)
    {
        for (var t = endpointType.BaseType; t is not null; t = t.BaseType)
        {
            if (t.Name == "Mutation" && t.TypeArguments.Length == 1 && t.TypeArguments[0] is INamedTypeSymbol entity)
                return entity;
        }

        return null;
    }

    /// <summary>True for a form/file-bound member (IFormFile or [FromForm]) — the body is not JSON.</summary>
    private static bool IsFormBound(IPropertySymbol property) =>
        property.Type.Name is "IFormFile" or "IFormFileCollection" ||
        property.GetAttributes().Any(a => a.AttributeClass?.Name is "FromFormAttribute");

    /// <summary>
    ///     Heuristic foreign key: a <c>Guid</c> (or <c>Guid?</c>) field whose name ends in <c>Id</c>. A random
    ///     value would reference a non-existent parent and fail the create (FK violation), so it is treated as
    ///     un-synthesizable — real FK setup is topological synthesis (phase 3), not yet wired.
    /// </summary>
    internal static bool IsForeignKey(IPropertySymbol property)
    {
        var type = property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
            ? n.TypeArguments[0]
            : property.Type;
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid"
            && property.Name.EndsWith("Id", System.StringComparison.Ordinal);
    }
}
