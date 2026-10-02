using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     The <c>ILocalIdentityStore</c> of each <c>[PragmaticUser]</c> entity that owns a <c>LocalIdentity</c>:
///     written, and described to Composition for registration.
/// </summary>
/// <remarks>
///     <para>
///         Part of Persistence and not of Identity: the store saves through the boundary's unit of work,
///         and the boundary an entity belongs to is resolved here.
///     </para>
///     <para>
///         An application that writes its own store keeps it: when this compilation declares a class
///         implementing <c>ILocalIdentityStore</c>, nothing is generated. Both would be registered with
///         <c>TryAdd</c>, and which one answered would depend on registration order.
///     </para>
/// </remarks>
internal static class LocalIdentityStoreFeature
{
    private const string StoreInterface = "Pragmatic.Identity.Local.Services.ILocalIdentityStore";

    /// <summary>
    ///     Writes the stores and returns their registrations, for the <c>generatedServices</c> channel.
    /// </summary>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.ServiceModel>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<EntityMetadataModel>> entities,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var hasOwnStore = context.SyntaxProvider
            .CreateSyntaxProvider(
                // By name first, on the syntax: the semantic check below runs only for a class that says
                // ILocalIdentityStore in its base list, not for every class that has one.
                static (node, _) => node is ClassDeclarationSyntax { BaseList: { } bases }
                                    && bases.Types.Any(static t => t.Type.ToString().EndsWith("ILocalIdentityStore", StringComparison.Ordinal)),
                static (ctx, ct) => ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is INamedTypeSymbol type
                                    && type.AllInterfaces.Any(i => i.ToDisplayString() == StoreInterface))
            .Where(static implements => implements)
            .Collect()
            .Select(static (found, _) => found.Length > 0);

        var stores = entities.Combine(features).Combine(hasOwnStore)
            .Select(static (input, _) =>
            {
                var ((all, detected), ownStore) = input;
                return new EquatableArray<EntityMetadataModel>(StoresFor(all, detected, ownStore));
            });

        context.RegisterSourceOutputSafe(stores, static (ctx, users) =>
        {
            foreach (var user in users)
            {
                var artifact = new LocalIdentityStoreTemplate(user).RenderOutput();
                if (!artifact.IsEmpty)
                    ctx.AddSource(artifact);
            }
        });

        return stores.Select(static (users, _) => Services(users));
    }

    /// <summary>The entities that get a store: local, valid, holding a LocalIdentity, with nothing written by hand.</summary>
    private static ImmutableArray<EntityMetadataModel> StoresFor(
        ImmutableArray<EntityMetadataModel> entities, DetectedFeatures features, bool ownStore)
    {
        if (ownStore || !features.HasPersistenceEFCore || entities.IsDefaultOrEmpty)
            return ImmutableArray<EntityMetadataModel>.Empty;

        return entities
            .Where(static e => e.IsValid && !e.IsFromReference && !string.IsNullOrEmpty(e.LocalIdentityProperty))
            .ToImmutableArray();
    }

    /// <summary>
    ///     One scoped <c>ILocalIdentityStore</c> per store. Its dependencies are the repository and the
    ///     boundary's unit of work, both registered by Persistence.
    /// </summary>
    private static EquatableArray<Composition.Models.ServiceModel> Services(EquatableArray<EntityMetadataModel> users)
    {
        var services = ImmutableArray.CreateBuilder<Composition.Models.ServiceModel>();

        foreach (var user in users)
        {
            var entity = $"global::{user.FullTypeName}";
            var store = $"{entity}.{LocalIdentityStoreTemplate.ClassName}";

            services.Add(new Composition.Models.ServiceModel
            {
                Namespace = user.Namespace,
                TypeName = LocalIdentityStoreTemplate.ClassName,
                FullTypeName = store,
                ServiceTypeName = $"global::{StoreInterface}",
                Lifetime = "Scoped",
                AsSelf = false,
                // Both are registered by the host, and this description has to say so: a dependency
                // built from names has no symbol to read [ProvidedByHost] from.
                Dependencies = ImmutableArray.Create(
                    new Composition.Models.DependencyModel
                    {
                        TypeName = "IRepository",
                        FullTypeName = $"global::Pragmatic.Persistence.Repository.IRepository<{entity}>",
                        IsOptional = false,
                        IsProvidedByHost = true,
                        ProvidedByHostLifetime = "Scoped"
                    },
                    new Composition.Models.DependencyModel
                    {
                        TypeName = "IUnitOfWork",
                        FullTypeName = "global::Pragmatic.Persistence.Repository.IUnitOfWork",
                        IsOptional = false,
                        Key = user.BoundaryTypeFullName,
                        IsProvidedByHost = true,
                        ProvidedByHostLifetime = "Scoped"
                    })
            });
        }

        return services.ToImmutable();
    }
}
