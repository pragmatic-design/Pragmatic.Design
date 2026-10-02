using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Generates entity core code: EntityConfiguration, Create factory, Relations,
///     Setters (with cascade event support), Specifications, and StateMachine.
///     Uses per-entity emission: changing one entity regenerates only that entity's artifacts.
/// </summary>
internal static class EntityCoreFeature
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(EntityMetadataModel Entity, DetectedFeatures Features)> perEntityWithFeatures,
        IncrementalValueProvider<(ImmutableArray<EntityMetadataModel> Entities, DetectedFeatures Features)> entitiesWithFeatures)
    {
        // Per-entity outputs: Relations, Create, Specs, Traits, Diagnostics
        // Only regenerated when the specific entity changes
        context.RegisterSourceOutputSafe(perEntityWithFeatures, static (ctx, pair) =>
        {
            if (!pair.Features.HasPersistenceEFCore)
                return;
            GeneratePerEntityOutputs(ctx, pair.Entity, pair.Features);
        });

        // PRAG0705 — required navigation to a [SoftDelete] target (INNER JOIN hides the dependent).
        // Aggregate, not per-entity: deciding whether the target owns the dependent needs the target's
        // own navigations, which only the full entity set carries.
        context.RegisterSourceOutputSafe(entitiesWithFeatures, static (ctx, pair) =>
        {
            if (!pair.Features.HasPersistenceEFCore)
                return;
            Validation.SoftDeleteNavigationValidator.Validate(ctx, pair.Entities);
        });

        // [CascadeOn<TSource>] property attribute → cascade handler generation
        var features = entitiesWithFeatures.Select(static (p, _) => p.Features);

        var cascadeProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CascadeTransform.CascadeOnAttributeName,
                predicate: static (node, _) => node is PropertyDeclarationSyntax,
                transform: CascadeTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutputSafe(cascadeProvider.Combine(features), static (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateCascadeHandler(ctx, pair.Left);
        });

        // Collect all cascade handlers into one DI registration so the domain-event dispatcher can
        // resolve and invoke them. Generated in the same assembly as the (internal) handlers.
        context.RegisterSourceOutputSafe(cascadeProvider.Collect().Combine(features), static (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore || pair.Left.IsDefaultOrEmpty)
                return;
            GenerateCascadeRegistration(ctx, pair.Left);
        });

        // [CascadeSource] property attribute → explicit cross-project cascade source declaration
        var cascadeSourceProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CascadeSourceTransform.CascadeSourceAttributeName,
                predicate: static (node, _) => node is PropertyDeclarationSyntax,
                transform: CascadeSourceTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect();

        // Combine both cascade source signals into a map
        var cascadeSourcesProvider = cascadeProvider
            .Collect()
            .Combine(cascadeSourceProvider)
            .Select(static (pair, _) => BuildCascadeSourceMap(pair.Left, pair.Right));

        // Entity.Set{Property} for private setters (enriched with cascade event emission)
        // Per-entity: only regenerate setters for the entity that changed
        var settersProvider = perEntityWithFeatures.Combine(cascadeSourcesProvider);
        context.RegisterSourceOutputSafe(settersProvider, static (ctx, pair) =>
        {
            var ((entity, detectedFeatures), cascadeSources) = pair;
            if (!detectedFeatures.HasPersistenceEFCore)
                return;
            GenerateEntitySetters(ctx, entity, cascadeSources);
        });

        // StateMachine methods for entities with [StateMachine<TEnum>]
        var stateMachineProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                StateMachineTransform.StateMachineAttributeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: StateMachineTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(stateMachineProvider, static (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateStateMachine(ctx, pair.Left);
        });

        // [GeneratedValue(format)] with AutoGenerate → IDefaultValueGenerator that formats the value.
        // Sequence-backed formats are excluded by the transform (handled separately).
        var alternateKeyProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratedValueTransform.GeneratedValueAttributeName,
                predicate: static (node, _) => node is PropertyDeclarationSyntax,
                transform: GeneratedValueTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(alternateKeyProvider, static (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateValueGenerator(ctx, pair.Left);
        });

        // The registration is driven by the entity, not by a create mutation. Deriving it from the
        // mutations that carried a computed default meant an entity with a generated value and no
        // create mutation produced a generator nothing registered, and the column reached the
        // database empty with no error anywhere.
        var valueRegistrationProvider = alternateKeyProvider.Collect();

        context.RegisterSourceOutputSafe(valueRegistrationProvider, static (ctx, pairs) =>
        {
            if (pairs.Length == 0 || !pairs[0].Right.HasPersistenceEFCore)
                return;

            GenerateValueRegistration(ctx, pairs.Select(static p => p.Left).ToImmutableArray());
        });
    }

    private static void GenerateValueRegistration(
        SourceProductionContext context,
        ImmutableArray<GeneratedValueModel> values)
    {
        var template = new GeneratedValueRegistrationTemplate(values);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateValueGenerator(
        SourceProductionContext context,
        GeneratedValueModel model)
    {
        var template = new GeneratedValueTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    // =========================================================================
    // Per-entity generation — all non-setter artifacts for a single entity
    // =========================================================================

    private static void GeneratePerEntityOutputs(
        SourceProductionContext context,
        EntityMetadataModel entity,
        DetectedFeatures features)
    {
        if (!entity.IsValid || entity.IsFromReference)
            return;

        // Diagnostics
        ReportEntityDiagnostics(context, entity);
        ReportUnstorableProtectedValues(context, entity, features);

        // Entity.Relations (FK, collection, ref nav properties from [Relation.*])
        if (entity.HasGeneratedRelationProperties)
        {
            var relationsTemplate = new EntityRelationsTemplate(entity);
            var relArtifact = relationsTemplate.RenderOutput();
            if (!relArtifact.IsEmpty)
                context.AddSource(relArtifact);
        }

        // Entity.Create factory methods
        var createTemplate = new EntityCreateTemplate(entity);
        var createArtifact = createTemplate.RenderOutput();
        if (!createArtifact.IsEmpty)
            context.AddSource(createArtifact);

        // Specifications
        var specTemplate = new SpecificationTemplate(entity);
        var specArtifact = specTemplate.RenderOutput();
        if (!specArtifact.IsEmpty)
            context.AddSource(specArtifact);

        // Entity.Traits (PersistenceId, Id, IAuditable, ISoftDelete auto-generated)
        var traitsTemplate = new EntityTraitsTemplate(entity);
        var traitsArtifact = traitsTemplate.RenderOutput();
        if (!traitsArtifact.IsEmpty)
            context.AddSource(traitsArtifact);

        // Entity.Ownership (OwnerId, SetOwnerId, IOwnedEntity)
        if (entity.IsOwnedEntity)
        {
            var ownershipTemplate = new OwnershipPropertyTemplate(entity);
            var ownershipArtifact = ownershipTemplate.RenderOutput();
            if (!ownershipArtifact.IsEmpty)
                context.AddSource(ownershipArtifact);
        }

        // Entity.Scoping (AccessScopes, GrantScope, RevokeScope, IScopedEntity)
        if (entity.IsScopedEntity)
        {
            var scopedTemplate = new ScopedPropertyTemplate(entity);
            var scopedArtifact = scopedTemplate.RenderOutput();
            if (!scopedArtifact.IsEmpty)
                context.AddSource(scopedArtifact);
        }
    }

    // =========================================================================
    // Individual generate methods
    // =========================================================================

    /// <summary>
    ///     Emits <c>{Entity}.Setters.g.cs</c> — the single owner of a declared entity's setters.
    /// </summary>
    /// <remarks>
    ///     An entity that derives from another entity gets the lightweight template. The full one
    ///     declares the <c>IChangeTracking</c> infrastructure, which the base's partial already carries:
    ///     re-declaring it hides the base members (CS0108) and gives the object a second
    ///     <c>_modifiedProperties</c>, so a setter on the derived would record into a set that nobody
    ///     reads. <c>PersistenceFeature.GenerateDerivedTypeSetters</c> covers the derived types that are
    ///     NOT declared entities; the two must not overlap, because both write this hint name and Roslyn
    ///     discards the entire generator's output on a duplicate (CS8785 — a warning).
    /// </remarks>
    private static void GenerateEntitySetters(
        SourceProductionContext context,
        EntityMetadataModel entity,
        ImmutableDictionary<string, ImmutableHashSet<string>> cascadeSources)
    {
        if (!entity.IsValid || !entity.HasPrivateSetterProperties)
            return;
        if (entity.IsFromReference)
            return;

        if (entity.BaseEntityFullTypeName is not null)
        {
            var derivedArtifact = new DerivedEntitySettersTemplate(entity).RenderOutput();
            if (!derivedArtifact.IsEmpty)
                context.AddSource(derivedArtifact);
            return;
        }

        cascadeSources.TryGetValue(entity.FullTypeName, out var cascadeProps);

        var template = new EntitySettersTemplate(entity, cascadeProps);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateStateMachine(
        SourceProductionContext context,
        StateMachineModel model)
    {
        // Validate state machine graph integrity
        var diagnostics = Validation.StateMachineValidator.Validate(model, Location.None);
        foreach (var diagnostic in diagnostics)
            context.ReportDiagnostic(diagnostic);

        // Nothing to generate against a property that is not there, and emitting it anyway buries
        // PRAG0623 under the compiler errors it exists to replace.
        if (!model.PropertyExists)
            return;

        var template = new StateMachineTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateCascadeHandler(
        SourceProductionContext context,
        CascadeModel model)
    {
        // Fail closed. The handler filters on a foreign key by convention; without it the generated
        // file names a member the entity does not have, which surfaces as CS1061 in code the author
        // cannot open.
        if (!model.HasForeignKey)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.CascadeTargetWithoutForeignKey,
                model.Location?.ToLocation(),
                model.TargetTypeName,
                model.TargetProperty,
                model.SourceTypeName,
                model.ForeignKeyProperty));
            return;
        }

        var template = new CascadeHandlerTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateCascadeRegistration(
        SourceProductionContext context,
        ImmutableArray<CascadeModel> cascades)
    {
        var template = new CascadeHandlerRegistrationTemplate(cascades);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    ///     Builds a map from source entity FQN to the set of property names that trigger cascades.
    /// </summary>
    internal static ImmutableDictionary<string, ImmutableHashSet<string>> BuildCascadeSourceMap(
        ImmutableArray<CascadeModel> cascades,
        ImmutableArray<CascadeSourceInfo> explicitSources)
    {
        if (cascades.Length == 0 && explicitSources.Length == 0)
            return ImmutableDictionary<string, ImmutableHashSet<string>>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<string>>();

        foreach (var cascade in cascades)
        {
            if (!builder.TryGetValue(cascade.SourceFullTypeName, out var props))
                props = ImmutableHashSet<string>.Empty;
            builder[cascade.SourceFullTypeName] = props.Add(cascade.SourceProperty);
        }

        foreach (var source in explicitSources)
        {
            if (!builder.TryGetValue(source.EntityFullTypeName, out var props))
                props = ImmutableHashSet<string>.Empty;
            builder[source.EntityFullTypeName] = props.Add(source.PropertyName);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     PRAG0652 — a <c>ProtectedValue</c> property in an assembly that cannot store one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The configuration this generator writes names <c>ProtectedValueConverter</c>, so without
    ///         the package the build fails on generated source with a message about a file nobody
    ///         wrote. Said here it names the property and the package. There is nothing to fall back
    ///         to: the property would have no mapping at all, which is how
    ///         <c>ErasureStrategy.DestroyKey</c> came to be declarable and impossible.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Host mode only</b>, because the entity configuration is generated there and
    ///         nowhere else — "only the host knows the database provider". A boundary library declares
    ///         the entity and takes <c>Pragmatic.Cryptography</c> for the type; which package stores it
    ///         is a storage decision, and storage decisions belong to the host. Reported in the module
    ///         this refused a correct layout, which is how Time off met it.
    ///     </para>
    /// </remarks>
    private static void ReportUnstorableProtectedValues(
        SourceProductionContext context,
        EntityMetadataModel entity,
        DetectedFeatures features)
    {
        if (!features.IsHostMode || features.HasCryptographyEFCore)
            return;

        foreach (var prop in entity.Properties)
        {
            if (!ProtectedValueType.Matches(prop.TypeName))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceDiagnostics.ProtectedValueNeedsCryptographyEFCore,
                Location.None,
                prop.Name,
                entity.TypeName));
        }
    }

    private static void ReportEntityDiagnostics(
        SourceProductionContext context,
        EntityMetadataModel entity)
    {
        // [Relation.*] rule violations (PRAG0612-PRAG0615), validated against symbols at transform time.
        foreach (var relationDiagnostic in entity.RelationDiagnostics)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceDiagnostics.ForRelation(relationDiagnostic.Kind),
                relationDiagnostic.Location?.ToLocation(),
                relationDiagnostic.Arguments.Cast<object?>().ToArray()));
        }

        // PRAG0624: a trait's properties declared by hand, but only some of them. Without this the
        // author gets a CS0102 per property they wrote, against a generated file they never
        // opened, and the obvious reading of it — delete them — is the opposite of the fix.
        foreach (var partialTrait in entity.PartialTraits)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceDiagnostics.PartialTraitProperties,
                entity.DeclarationLocation?.ToLocation(),
                entity.TypeName,
                partialTrait.TraitName,
                string.Join(", ", partialTrait.MissingProperties)));
        }

        // PRAG0625: the parts of one composite key asking for different scopes. Reported rather than
        // resolved, because either answer is a working schema that means something the author did not
        // write — and the wrong one is a uniqueness rule nobody notices until two tenants collide.
        if (entity.LogicKeys.Length > 1)
        {
            var parts = entity.LogicKeys.AsImmutableArray();
            if (parts.Any(p => p.IsGlobal) && parts.Any(p => !p.IsGlobal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PersistenceDiagnostics.MixedLogicKeyScope,
                    entity.DeclarationLocation?.ToLocation(),
                    entity.TypeName,
                    string.Join(", ", parts.Select(p => $"{p.Name}={(p.IsGlobal ? "Global" : "PerTenant")}"))));
            }
        }

        // PRAG0636: a class-level [LogicKey] part that resolved to nothing — neither a declared
        // property nor a key the relation graph generates on this entity. It arrives with an empty
        // type, because the type is the one thing a name that matches nothing cannot have.
        foreach (var part in entity.LogicKeys)
        {
            if (part.TypeName.Length > 0)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceDiagnostics.LogicKeyPartNotFound,
                entity.DeclarationLocation?.ToLocation(),
                entity.TypeName,
                part.Name));
        }

        // PRAG0637: the key declared on the class and on properties at once.
        if (entity.LogicKeyDeclaredTwice)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceDiagnostics.LogicKeyDeclaredTwice,
                entity.DeclarationLocation?.ToLocation(),
                entity.TypeName));
        }

        // PRAG0626: a tenant-scoped entity that assigns its own primary key. The tenant filter hides
        // rows; it cannot make two identical primary keys coexist, and the only repair reaches every
        // foreign key in the schema.
        if (entity is { IsTenantEntity: true, HasManualPersistenceId: true })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceDiagnostics.TenantEntityWithManualId,
                entity.DeclarationLocation?.ToLocation(),
                entity.TypeName));
        }

        var propertyNames = new HashSet<string>(entity.Properties.Select(p => p.Name));
        foreach (var gp in entity.GeneratedRelationProperties)
            propertyNames.Add(gp.Name);

        // PRAG0627: a [Unique] naming a property that does not exist. The names arrive as strings, so
        // nameof is a convention and not a guarantee; without this the author gets a CS1061 inside a
        // generated file, naming a property on a type they did not write the configuration for.
        foreach (var index in entity.UniqueIndexes)
        {
            foreach (var column in index.Columns)
            {
                // TenantId is added by the generator, not declared, so it is not in Properties.
                if (propertyNames.Contains(column) || column == "TenantId")
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    PersistenceDiagnostics.UniqueIndexPropertyNotFound,
                    entity.DeclarationLocation?.ToLocation(),
                    entity.TypeName,
                    column));
            }
        }
        // PRAG0651 asks the author to supply what EF Core cannot infer. Two kinds of property look
        // unmappable to a name-based check and are already mapped — by the configuration this same
        // generator writes a few files away: a [ValueObject] becomes ComplexProperty(...), and a
        // collection of primitives becomes Property(...) as an EF Core primitive collection. Warning
        // about those points the author at the generator's own output and asks them to fix it.
        var primitiveCollections = new HashSet<string>(
            entity.PrimitiveCollectionProperties.Select(c => c.Name), StringComparer.Ordinal);

        // ⚠️ And a third: a navigation the author declared by hand. MayNeedValueConverter is a check
        // on the type's NAME, so a property holding another entity is "not a known scalar" and looked
        // exactly like one that needs a converter. EF Core maps it as a relationship, which this same
        // generator configures — so the warning pointed at the generator's own output again. Only
        // navigations the entity declares are exempt: one nobody declared is a property EF cannot
        // infer either.
        var navigations = new HashSet<string>(
            entity.Navigations.Select(n => n.Name), StringComparer.Ordinal);

        // And a fourth: a LocalizedString, which EntityConfigurationTemplate maps to a JSON column with a
        // converter of its own.
        //
        // And a fifth: Money. EntityConfigurationTemplate.RenderMoneyComplexProperty maps it as an EF Core
        // complex type — Amount as a native decimal and Currency converted to its three letters — which is
        // exactly the mapping this warning asks the author to supply. An entity could therefore not hold a
        // Money at all in a project that treats warnings as errors, which is every Pragmatic project: the
        // first application to bill anybody met seven of these and none of them was a defect of its own.
        // And a sixth: a ProtectedValue. EntityConfigurationTemplate now gives it
        // ProtectedValueConverter, which is the mapping this warning would ask the author to supply —
        // and could not be supplied, since there is no seam on the generated context.
        foreach (var prop in entity.Properties)
        {
            if (prop.IsEnum || prop.IsValueObject || primitiveCollections.Contains(prop.Name)
                || navigations.Contains(prop.Name) || LocalizedStringType.Matches(prop.TypeName)
                || ProtectedValueType.Matches(prop.TypeName)
                || IsMoney(prop.TypeName))
                continue;

            if (MayNeedValueConverter(prop.TypeName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PersistenceDiagnostics.PropertyTypeMayNeedConverter,
                    Location.None,
                    prop.Name,
                    entity.TypeName,
                    prop.TypeName));
            }
        }

        // Ownership: info when dev manually declares OwnerId
        if (entity is { IsOwnedEntity: true, HasManualOwnedEntityProps: true })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                OwnershipDiagnostics.OwnedEntityManualOwnerId,
                Location.None,
                entity.TypeName));
        }
    }

    /// <summary>The type <c>RenderMoneyComplexProperty</c> configures, nullable or not.</summary>
    private static bool IsMoney(string typeName)
        => typeName.TrimEnd('?') == "Pragmatic.Internationalization.Types.Money";

    private static bool MayNeedValueConverter(string typeName)
    {
        var type = typeName.TrimEnd('?');
        if (type.StartsWith("System.Nullable<", StringComparison.Ordinal))
            type = type.Substring("System.Nullable<".Length).TrimEnd('>');

        if (type is "string" or "System.String" or
            "bool" or "System.Boolean" or
            "byte" or "System.Byte" or
            "short" or "System.Int16" or
            "int" or "System.Int32" or
            "long" or "System.Int64" or
            "float" or "System.Single" or
            "double" or "System.Double" or
            "decimal" or "System.Decimal" or
            "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan" or
            "System.Guid" or
            "byte[]" or "System.Byte[]" or
            "char" or "System.Char" or
            "sbyte" or "System.SByte" or
            "ushort" or "System.UInt16" or
            "uint" or "System.UInt32" or
            "ulong" or "System.UInt64" or
            "System.DateOnly" or "System.TimeOnly")
        {
            return false;
        }

        return true;
    }
}
