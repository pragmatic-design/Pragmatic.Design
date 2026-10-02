using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Mapping.Models;
using Pragmatic.SourceGenerator.Features.Patch.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Generates query-related code: Query, GridFilter, FilterDto, GridBridge,
///     GridAdapter, QueryView, and Patch.
/// </summary>
internal static class QueryFeature
{
    /// <summary>
    ///     Registers the query generators, and hands back the routes derived from specifications.
    /// </summary>
    /// <remarks>
    ///     The derived query is a type this generator writes, so it is never a symbol the endpoint
    ///     feature could find: its route travels as a model, on the same channel <c>[Resource]</c> and
    ///     the traits already use. That is the reason for the return value — the derivation happens here,
    ///     and the endpoints are assembled two features later.
    /// </remarks>
    public static (IncrementalValueProvider<ImmutableArray<Endpoints.Models.EndpointModel>> DerivedEndpoints,
        IncrementalValueProvider<ImmutableArray<QueryModel>> QueryModels) Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<QueryModel>>? resourceQueries = null)
    {
        // Every query this assembly declares, however it was declared, collected for the invoker pass.
        // That pass runs later than this one because it needs the permission catalog, which is built
        // from models this feature has not produced yet when Register is called.
        var collected = new List<IncrementalValueProvider<ImmutableArray<QueryModel>>>();

        // Query<TEntity, TResult> attribute generator
        var queryModels2 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Persistence.Query.Attributes.QueryAttribute`2",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: QueryTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // A query whose projected type does not resolve is dropped here, after GenerateQueryCode has
        // reported it: everything downstream — the invoker, the three boundary facades — would name a
        // type that cannot exist.
        collected.Add(queryModels2.Where(static m => m.IsValid).Collect());
        var queryProvider2 = queryModels2.Combine(features);

        context.RegisterSourceOutputSafe(queryProvider2, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateQueryCode(ctx, pair.Left);
        });

        // Query<TEntity> attribute generator (where TResult = TEntity)
        var queryModels1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Persistence.Query.Attributes.QueryAttribute`1",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: QueryTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        collected.Add(queryModels1.Where(static m => m.IsValid).Collect());
        var queryProvider1 = queryModels1.Combine(features);

        context.RegisterSourceOutputSafe(queryProvider1, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateQueryCode(ctx, pair.Left);
        });

        // The same two attributes on a specification — a static member returning Specification<TEntity>.
        // The derived type and the members the ordinary templates write for it are both generated here,
        // in the same compilation, so the two halves of the partial class meet.
        // One collection per attribute arity, joined below: the two providers are two readings of the
        // same shape, and a route declared through either has to reach the same table.
        var derivedByArity = new List<IncrementalValueProvider<ImmutableArray<DerivedQueryModel>>>();
        var derivedModels = new List<IncrementalValuesProvider<DerivedQueryModel>>();

        foreach (var attributeName in new[]
                 {
                     "Pragmatic.Persistence.Query.Attributes.QueryAttribute`2",
                     "Pragmatic.Persistence.Query.Attributes.QueryAttribute`1"
                 })
        {
            var described = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    attributeName,
                    predicate: static (node, _) => node is MethodDeclarationSyntax or PropertyDeclarationSyntax,
                    transform: SpecificationQueryTransform.Transform);

            // PRAG0729. A [Query] on a member the transform declines produces nothing, so it must not
            // also say nothing: the transform is total, the reason travels and is reported here.
            context.RegisterSourceOutputSafe(described, static (ctx, model) =>
            {
                if (model.Rejection == DerivedQueryRejection.None)
                    return;

                ctx.ReportDiagnostic(
                    QueryPipelineDiagnostics.SpecificationQueryDerivesNothing,
                    model.Location?.ToLocation(),
                    model.DeclaredOn,
                    Explain(model.Rejection));
            });

            var models = described.Where(static m => m.IsValid);

            derivedModels.Add(models);
            derivedByArity.Add(models.Collect());
            collected.Add(models.Select(static (m, _) => ToQueryModel(m)).Collect());
        }

        var derivedQueries = derivedByArity.Aggregate(static (left, right) =>
            left.Combine(right).Select(static (pair, _) => pair.Left.AddRange(pair.Right)));

        // PRAG0726, on the collected array because a single model cannot see its siblings: two
        // specifications in one namespace whose members share a name derive one type twice, and two
        // generated files would carry one hint. Reported here rather than left to Roslyn, which drops
        // the whole generator's output for a duplicate hint under a warning.
        context.RegisterSourceOutputSafe(derivedQueries.Combine(features), static (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;

            foreach (var group in CollidingDerivedNames(pair.Left))
            {
                var colliding = group.ToList();
                ctx.ReportDiagnostic(
                    QueryPipelineDiagnostics.DerivedQueryNameCollides,
                    colliding[1].Location?.ToLocation(),
                    $"{colliding[0].ContainerFullTypeName}.{colliding[0].MemberName}",
                    $"{colliding[1].ContainerFullTypeName}.{colliding[1].MemberName}",
                    group.Key);
            }
        });

        // The emission is filtered by the same reading, not only reported on. Roslyn answers a
        // duplicate hint by discarding this generator's whole output, the diagnostics with it, so a
        // PRAG0726 that is merely reported never reaches the author: the build shows CS8785 instead.
        var collidingNames = derivedQueries.Select(static (models, _) =>
            new EquatableArray<string>(
                CollidingDerivedNames(models).Select(static g => g.Key).ToImmutableArray()));

        foreach (var models in derivedModels)
        {
            context.RegisterSourceOutputSafe(
                models.Combine(features).Combine(collidingNames),
                (ctx, pair) =>
                {
                    if (!pair.Left.Right.HasPersistenceEFCore)
                        return;

                    var model = pair.Left.Left;
                    if (pair.Right.Contains($"{model.Namespace}.{model.TypeName}"))
                        return;

                    var artifact = new DerivedQueryTemplate(model).RenderOutput();
                    if (!artifact.IsEmpty)
                        ctx.AddSource(artifact);

                    GenerateQueryCode(ctx, ToQueryModel(model));
                });
        }

        var derivedEndpoints = derivedQueries
            .Combine(features)
            .Select(static (pair, _) => pair.Right.HasPersistenceEFCore
                ? DerivedQueryEndpointBuilder.Build(pair.Left)
                : ImmutableArray<Endpoints.Models.EndpointModel>.Empty);

        // GridFilter<TEntity> attribute generator
        var gridFilterProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Persistence.Query.Attributes.GridFilterAttribute`1",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: GridFilterTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(gridFilterProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateGridFilterCode(ctx, pair.Left);
        });

        // [GenerateGridBridge] attribute generator
        var gridBridgeProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.GenerateGridBridge,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: GridFilterBridgeTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(gridBridgeProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateGridBridgeCode(ctx, pair.Left);
        });

        // QueryView<TRoot> attribute generator
        var queryViewProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Persistence.Query.Attributes.QueryViewAttribute`1",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: QueryViewTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(queryViewProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateQueryViewCode(ctx, pair.Left);
        });

        // GridAdapter<TEntity> attribute generator
        var gridAdapterProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Persistence.Query.Attributes.GridAdapterAttribute`1",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: GridAdapterTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(gridAdapterProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateGridAdapterCode(ctx, pair.Left);
        });

        // FilterDto<TEntity> attribute generator
        var filterDtoProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Persistence.Query.Attributes.FilterDtoAttribute`1",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: FilterDtoTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Combine(features);

        context.RegisterSourceOutputSafe(filterDtoProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GenerateFilterDtoCode(ctx, pair.Left);
        });

        // Patch DTOs in current compilation
        var currentPatchProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MutationMetadataTransform.PatchAttributeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: MutationMetadataTransform.Transform)
            .Combine(features);

        context.RegisterSourceOutputSafe(currentPatchProvider, (ctx, pair) =>
        {
            if (!pair.Right.HasPersistenceEFCore)
                return;
            GeneratePatchApply(ctx, pair.Left);
        });

        // Resource-generated queries (programmatic QueryModels from [Resource(Capabilities)])
        if (resourceQueries is not null)
        {
            var resourceQueriesWithFeatures = resourceQueries.Value
                .SelectMany(static (models, _) => models)
                .Combine(features);

            context.RegisterSourceOutputSafe(resourceQueriesWithFeatures, (ctx, pair) =>
            {
                if (!pair.Right.HasPersistenceEFCore)
                    return;
                GenerateQueryCode(ctx, pair.Left);
            });

            collected.Add(resourceQueries.Value);
        }

        var allQueryModels = collected.Aggregate(static (left, right) =>
            left.Combine(right).Select(static (pair, _) => pair.Left.AddRange(pair.Right)));

        return (derivedEndpoints, allQueryModels);
    }

    /// <summary>
    ///     Emits the nested <c>Invoker</c> of every declared query, once the permissions it names can be
    ///     resolved.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Separate from <see cref="Register" /> because of what it needs: a <c>[RequirePermission]</c>
    ///         on a query often names a constant this same generator writes, which no compilation can
    ///         bind while it is being written. The catalog that binds it is built from the entity models
    ///         this feature produces, so it does not exist yet when <c>Register</c> runs — the same
    ///         reason the endpoint feature resolves its own permissions a stage later.
    ///     </para>
    ///     <para>
    ///         ⚠️ A path that resolves to nothing is dropped, and the invoker then enforces nothing. It is
    ///         reported as <c>PRAG0725</c> rather than left silent: a permission that was declared and
    ///         quietly not enforced is the failure this whole slab exists to close.
    ///     </para>
    /// </remarks>
    public static void RegisterInvokers(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<QueryModel>> queryModels,
        IncrementalValueProvider<EquatableArray<Core.PermissionConstEntry>> permissionCatalog,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<Actions.Models.PermissionDerivationInputs> derivation,
        IncrementalValueProvider<EquatableArray<Identity.Models.UserEntityModel>> currentUsers)
    {
        var resolved = queryModels
            .Combine(permissionCatalog)
            .Select(static (pair, _) =>
            {
                var index = Compositions.PermissionCatalogLookup.Index(pair.Right);
                return pair.Left.Select(model => ResolvePermissions(model, index)).ToImmutableArray();
            })
            .Combine(derivation)
            .Select(static (pair, _) =>
                pair.Left.Select(model => DerivePermission(model, pair.Right)).ToImmutableArray())
            // [FromCurrentUser] reads the user entity, which is another declaration of the compilation:
            // it arrives here through the pipeline rather than being searched for by each query.
            .Combine(currentUsers)
            .Select(static (pair, _) =>
                pair.Left.Select(model => CurrentUserBindingTransform.Resolve(model, pair.Right)).ToImmutableArray())
            .Combine(features);

        context.RegisterSourceOutputSafe(resolved, (ctx, pair) =>
        {
            // Before the gate: a binding that cannot be generated is an error whether or not an invoker
            // is written, and one reported only beside its output would go silent with it.
            foreach (var model in pair.Left)
                ReportCurrentUserBindings(ctx, model);

            // The same gate as the rest of this feature, and it has to be: the invoker's read hands the
            // query to IQueryExecutor, which takes an IPagedQuery/IQuery — the interface the query gets
            // from the Apply generated beside it. Gating this on EF Core alone was more permissive than
            // that, and a module with a [Query] and no Pragmatic persistence got an invoker for a query
            // that implements neither interface: `Pragmatic.Actions.Samples` stopped compiling.
            if (!pair.Right.HasPersistenceEFCore)
                return;

            foreach (var model in pair.Left)
                GenerateQueryInvoker(ctx, model);
        });
    }

    /// <summary>
    ///     The query with the name the posture gives it, or the query untouched.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The same <c>ResolvePermission</c> the endpoint asks, with the same inputs, because the
    ///     two doors have to agree. Asked only on the endpoint model — the route's configuration — it
    ///     would protect the route and nothing else: a read declaring nothing would be refused over
    ///     HTTP and served in process through the boundary facade, which builds this very invoker.
    /// </remarks>
    private static QueryModel DerivePermission(
        QueryModel model, Actions.Models.PermissionDerivationInputs inputs)
    {
        var resolved = Actions.ActionsFeature.ResolvePermission(
            model.IsValid,
            model.AllowAnonymous,
            hasPermissionRequirement: model.RequiredPermissions.Count > 0,
            hasUnresolvedPaths: model.UnresolvedPermissionPaths.Count > 0,
            model.ExplicitPermission,
            model.TypeName,
            model.Namespace,
            model.BoundaryTypeName,
            inputs.Boundaries.AsImmutableArray(),
            Compositions.PermissionCatalogLookup.Index(inputs.Catalog));

        if (!inputs.Enabled || resolved is null)
            return model;

        return model with
        {
            RequiredPermissions = new EquatableArray<string>(
                ImmutableArray.Create(resolved.Value.Name))
        };
    }

    /// <summary>Binds the constant paths a query named, and keeps what still did not bind.</summary>
    private static QueryModel ResolvePermissions(QueryModel model, Dictionary<string, string> index)
    {
        if (model.UnresolvedPermissionPaths.Count == 0)
            return model;

        var bound = model.RequiredPermissions.AsImmutableArray().ToBuilder();
        var stillUnresolved = ImmutableArray.CreateBuilder<string>();

        foreach (var path in model.UnresolvedPermissionPaths)
        {
            var value = Compositions.PermissionCatalogLookup.Resolve(path, index);
            if (value is not null)
                bound.Add(value);
            else
                stillUnresolved.Add(path);
        }

        return model with
        {
            RequiredPermissions = bound.ToImmutable(),
            UnresolvedPermissionPaths = stillUnresolved.ToImmutable()
        };
    }

    /// <summary>
    ///     PRAG0730 and PRAG0731, for each <c>[FromCurrentUser]</c> property of the query, and PRAG0734
    ///     for each <c>[FromClock]</c> one.
    /// </summary>
    private static void ReportCurrentUserBindings(SourceProductionContext context, QueryModel model)
        => InvokerBindingReporter.Report(context, model.TypeName, model.CurrentUserBindings, model.ClockBindings);

    private static void GenerateQueryInvoker(SourceProductionContext context, QueryModel model)
    {
        if (!model.IsPartial)
            return;

        foreach (var path in model.UnresolvedPermissionPaths)
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.QueryPermissionDoesNotResolve,
                model.Location?.ToLocation(), model.TypeName, path));

        var artifact = new QueryInvokerTemplate(model).RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    // =========================================================================
    // Generate methods
    // =========================================================================

    /// <summary>
    ///     The derived query, described the way a hand-written one is.
    /// </summary>
    /// <remarks>
    ///     The inputs are <b>not</b> filters: they are read by the specification, which is the same thing
    ///     <c>[BindSpecification]</c> says on a hand-written query. Listing them as filters would generate
    ///     a second, silent <c>Where</c> beside the rule's own.
    /// </remarks>
    /// <summary>
    ///     The derived names two or more specifications would write to the same file.
    /// </summary>
    /// <remarks>
    ///     One reading for two consumers: the diagnostic that names them, and the emission that has to
    ///     skip them. Read twice, the two could disagree, and the half that would then be wrong is the
    ///     one whose failure is a warning nobody sees.
    /// </remarks>
    /// <summary>What to tell the author, per reason.</summary>
    /// <remarks>
    ///     One sentence each, naming the thing that is wrong rather than the rule it broke. The two
    ///     that are believed unreachable are here too: a guard that fires when it was thought it could
    ///     not is the case most worth hearing about.
    /// </remarks>
    private static string Explain(DerivedQueryRejection rejection) => rejection switch
    {
        DerivedQueryRejection.NotAMember => "the attribute is on something that is neither a method nor a property",
        DerivedQueryRejection.NotStatic => "the member is not static, so the rule cannot be read without an instance",
        DerivedQueryRejection.SpecificationTypeMissing => "Pragmatic.Specification is not referenced by this project",
        DerivedQueryRejection.NotASpecification => "it does not return a Specification<TEntity>",
        DerivedQueryRejection.NoResultType => "the attribute names no usable result type",
        DerivedQueryRejection.GenericContainer => "it is declared in a generic type, which the derived query cannot be",
        _ => "the reason was not recorded"
    };

    private static ImmutableArray<IGrouping<string, DerivedQueryModel>> CollidingDerivedNames(
        ImmutableArray<DerivedQueryModel> models)
        => models
            .GroupBy(static m => $"{m.Namespace}.{m.TypeName}", StringComparer.Ordinal)
            .Where(static g => g.Count() > 1)
            .ToImmutableArray();

    private static QueryModel ToQueryModel(DerivedQueryModel derived)
    {
        var properties = ImmutableArray.CreateBuilder<QueryPropertyModel>();

        foreach (var input in derived.Inputs)
        {
            properties.Add(new QueryPropertyModel
            {
                PropertyName = input.PropertyName,
                PropertyType = input.TypeName,
                IsNullable = input.IsNullable,
                IsFilter = false
            });
        }

        if (derived.Paged)
        {
            properties.Add(new QueryPropertyModel
            {
                PropertyName = "Page", PropertyType = "int", IsPageProperty = true
            });
            properties.Add(new QueryPropertyModel
            {
                PropertyName = "PageSize", PropertyType = "int", IsPageSizeProperty = true
            });
        }

        return new QueryModel
        {
            Namespace = derived.Namespace,
            TypeName = derived.TypeName,
            Accessibility = "public",
            TypeKind = "class",
            EntityTypeFullName = derived.EntityTypeFullName,
            EntityTypeName = derived.EntityTypeName,
            ResultTypeFullName = derived.ResultTypeFullName,
            ResultTypeName = derived.ResultTypeName,
            Properties = properties.ToImmutable(),
            IsPartial = true,
            Specifications = new EquatableArray<QuerySpecificationModel>(
                ImmutableArray.Create(new QuerySpecificationModel
                {
                    PropertyName = DerivedQueryModel.SpecificationPropertyName,
                    IsNullable = false
                })),
            // Same reason as the hand-written query's: the invoker reads from the boundary's
            // keyed context, and a derived query reads from the same one its route did.
            BoundaryTypeName = derived.BoundaryFullTypeName,
            SpecificationInputs = derived.Inputs.Select(i => i.PropertyName).ToImmutableArray(),
            Location = derived.Location
        };
    }

    private static void GenerateQueryCode(SourceProductionContext context, QueryModel model)
    {
        // PRAG9001 before everything, including the non-partial skip: a type that does not resolve is not this
        // query's rule being broken, it is a name the compiler never bound — and writing it back out is
        // what turned one authoring error into a page of CS0400 in generated files.
        if (model.UnresolvedResultType is { } unresolved)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                SourceGenerator.Diagnostics.GenerationDiagnostics.UnresolvedTypeStoppedGeneration,
                model.Location?.ToLocation(), model.TypeName, unresolved));
            return;
        }

        // Not partial: everything below writes members into the query's own type, which a non-partial
        // class cannot receive, and reporting the rest would bury the one that matters. PRAG0712 is the
        // companion analyzer's, on the declaration.
        if (!model.IsPartial)
            return;

        // PRAG0736: the path is not emitted, so what EF Core would have refused at the first request is refused here.
        foreach (var problem in model.EagerLoadProblems)
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.EagerLoadPathNamesNoNavigation, model.Location?.ToLocation(),
                model.TypeName, problem.Path, problem.Segment));

        // PRAG0737: the same reading for a join's Via. Before this it was copied into Apply unread, and
        // the author met it as a CS1061 at a line of a generated file.
        foreach (var join in model.Joins.Where(j => j is { IsNavigationJoin: true, UnresolvedSegment: not null }))
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.JoinPathNamesNoNavigation, model.Location?.ToLocation(),
                model.TypeName, join.TargetTypeName, join.Via, join.UnresolvedSegment));

        // PRAG0727: the ask was dropped, and dropping it in silence is what teaches nothing.
        if (model.RedundantPagingRequest)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.PagingRequestIsRedundant,
                model.Location?.ToLocation(), model.TypeName));
        }

        ReportUnrenderableFilters(context, model);

        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>
    ///     Reports the filter shapes the templates cannot render, before rendering them anyway.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every operator switch in the query, filter-DTO and grid templates ends in a default that
    ///         emits <c>==</c>. That default is right for an operator the shape does not support — a
    ///         <c>Contains</c> on an <c>int</c> has to become something — and wrong for
    ///         <c>Between</c>, which no branch anywhere implements: the author asked for a range and
    ///         got equality, silently, or a generated file that does not compile when the property is
    ///         the collection a range needs.
    ///     </para>
    ///     <para>
    ///         Generation continues so the rest of the file still appears; the error is what stops the
    ///         build.
    ///     </para>
    /// </remarks>
    private static void ReportUnrenderableFilters(SourceProductionContext context, QueryModel model)
    {
        // PRAG0709: the attribute claims a reader that does not exist. Reported per query rather than
        // per property, because a [BindSpecification] input leaves the property model on purpose — it
        // is an input, not a filter — so there is no per-property entry left to hang it on.
        if (!model.SpecificationInputs.IsDefaultOrEmpty && !model.HasSpecifications)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.BindSpecificationWithoutSpecification,
                model.Location?.ToLocation(),
                string.Join("', '", model.SpecificationInputs.AsImmutableArray()),
                model.TypeName));
        }

        // PRAG0723/PRAG0724: the canonical request is applied by a bridge the entity has to declare,
        // and it brings its own page with it.
        foreach (var grid in model.GridRequests)
        {
            if (!model.EntityDeclaresGridBridge)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.GridRequestWithoutBridge,
                    model.Location?.ToLocation(),
                    model.TypeName, grid.PropertyName, model.EntityTypeName));
            }

            if (model.HasPaging)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.GridRequestPagesTwice,
                    model.Location?.ToLocation(),
                    model.TypeName, grid.PropertyName));
            }
        }

        foreach (var property in model.Properties)
        {
            if (property.IsFilter && property.Operator == FilterOperatorKind.Between)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.BetweenOperatorNotSupported,
                    property.Location?.ToLocation(),
                    property.PropertyName,
                    model.TypeName));
            }

            if (property.IsInertInput)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.QueryInputGeneratesNoFilter,
                    property.Location?.ToLocation(),
                    property.PropertyName,
                    model.TypeName,
                    property.PropertyType));
            }

            if (property.HasInertSearchAcross)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.DeclaredOptionNotHonoured,
                    property.Location?.ToLocation(),
                    $"[SearchAcross] on '{property.PropertyName}'",
                    model.TypeName,
                    "a search matches text, and the property is not a string or names no column. "
                    + "Declare it 'string?' and name the entity's columns"));
            }

            if (property.HasInertFilterGroup)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.DeclaredOptionNotHonoured,
                    property.Location?.ToLocation(),
                    $"[FilterGroup] on '{property.PropertyName}'",
                    model.TypeName,
                    "it is read inside a [FilterDto<T>], on a property whose type is another filter "
                    + "DTO. On a query the attribute that carries a whole filter object is "
                    + "[ComplexFilter]"));
            }
        }

        ReportInertJoinOptions(context, model);

        if (model.InertProcessorCount > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.DeclaredOptionNotHonoured,
                model.Location?.ToLocation(),
                "[PreProcessor<T>] / [PostProcessor<T>]",
                model.TypeName,
                "the query handler template does not read them — they are rendered for Endpoint<T> "
                + "classes, mutations and domain actions. What runs on a query endpoint is the "
                + "endpoint's ResourcePolicy, an IResourceAuthorizer<TQuery>, and ISyncValidator on the "
                + "query itself"));
        }

        // The result type has to provide the Projection the template is about to name. Checked here
        // rather than left to the compiler: the error would otherwise land inside a generated file,
        // pointing at a member the author never wrote.
        // ⚠️ An aggregate view is the third way to reach a result and the reason this check exists at
        // all: it declares no Projection because a grouping cannot be one, and the query names its
        // Build instead. Without the exemption the diagnostic refuses exactly the read it was written
        // to protect — a declared aggregate — and sends the author back to hand-written LINQ.
        // ⚠️ A joined query is the fourth way, and the exemption is the same shape as the aggregate
        // view's: it declares no Projection because the joined columns cannot travel in one, and the
        // step it generates names the result's properties itself. PRAG0740 checks those.
        // ⚠️ And not when a key join was refused: the query asked for a joined step, the refusal is
        // already reported, and adding "your result type declares no Projection" on top would name a
        // member the author never wanted.
        if (!model.IsSameEntityAndResult && !model.ResultTypeGeneratesAProjection
            && !model.MapsInMemory && !model.ResultIsAggregateView && !model.GeneratesJoinedProjection
            && !model.HasRefusedKeyJoin)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.ResultTypeHasNoProjection,
                model.Location?.ToLocation(),
                model.TypeName,
                model.ResultTypeName));
        }
    }

    /// <summary>
    ///     Reports the <c>[Join]</c> declarations the generator cannot turn into a join.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <c>ForeignKey</c>, <c>Type</c> and <c>Alias</c> all generate — <c>Alias</c> is the
    ///         prefix that tells two joins to the same type apart in the result — so what is reported
    ///         here is the shapes that genuinely cannot be written, each an error at the author's own
    ///         declaration rather than a warning about an option that silently does nothing.
    ///     </para>
    ///     <para>
    ///         A warning survives for one case only: a join on a query whose result <b>is</b> the
    ///         entity is reported by <c>PRAG0738</c>, because there the loss is a whole feature and
    ///         not a spelling.
    ///     </para>
    /// </remarks>
    private static void ReportInertJoinOptions(SourceProductionContext context, QueryModel model)
    {
        var location = model.Location?.ToLocation();

        foreach (var join in model.Joins.Where(j => j.IsKeyJoin))
        {
            if (join.UnresolvedForeignKey is { } foreignKey)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.JoinKeyNamesNoProperty, location,
                    model.TypeName, join.TargetTypeName, foreignKey, model.EntityTypeName));
            }

            if (join.UnresolvedTargetKey is { } targetKey)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.JoinKeyNamesNoProperty, location,
                    model.TypeName, join.TargetTypeName, targetKey, join.TargetTypeName));
            }

            if (!join.IsGeneratableJoinType)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.JoinTypeCannotBeGenerated, location,
                    model.TypeName, join.TargetTypeName, join.JoinType));
            }

            // The target is in no context this query can reach, so there is no set to join against.
            // Reported whatever else is wrong with the declaration: it is the one that stops the join
            // existing at all, and the run time was the only thing saying so.
            if (!join.TargetIsReachable)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.JoinTargetIsOutsideTheBoundary, location,
                    model.TypeName, join.TargetTypeName, model.BoundaryTypeName ?? model.EntityTypeName));
            }

            if (!join.IsResolvedKeyJoin || !join.IsGeneratableJoinType)
                continue;

            // The join resolves and could be generated, but the query answers with the entity: there
            // is no result type to carry the joined columns into.
            if (model.IsSameEntityAndResult)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPipelineDiagnostics.KeyJoinNeedsAResultType, location,
                    model.TypeName, join.TargetTypeName));
            }
            else if (model.BoundaryTypeName is null)
            {
                Report($"[Join<{join.TargetTypeName}>(ForeignKey = \"{join.ForeignKey}\")]",
                    "the target's set is read from the root boundary's own DbContext — EF Core "
                    + "composes a join only inside one instance — and this query's entity names no "
                    + "boundary, so there is no context to read it from");
            }
        }

        // ⚠️ On a navigation join the two are still inert, and that is not an oversight: a resolved
        // Via contributes an include path and nothing else, so there is no join for Type to shape and
        // no second source for Alias to name. Only a key join gave them work.
        foreach (var join in model.Joins.Where(j => !j.IsKeyJoin))
        {
            if (join.JoinType != JoinTypeKind.Inner)
            {
                Report($"[Join<{join.TargetTypeName}>(Type = JoinType.{join.JoinType})]",
                    "a Via join is an include path, and an include has no join type. Type shapes a key "
                    + "join — [Join<T>(ForeignKey = …)] — where there is a join to shape; over a "
                    + "navigation, an optional relation (Required = false) is what makes it outer");
            }

            if (!string.IsNullOrEmpty(join.Alias))
            {
                Report($"[Join<{join.TargetTypeName}>(Alias = \"{join.Alias}\")]",
                    "nothing reads Alias on a Via join: two of them are told apart by their paths. On a "
                    + "key join it is the prefix that says which target a result property comes from");
            }
        }

        foreach (var property in model.UnresolvedResultProperties)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.JoinedResultPropertyHasNoSource, location,
                model.TypeName, model.ResultTypeName, property));
        }

        void Report(string option, string explanation)
            => context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.DeclaredOptionNotHonoured,
                location, option, model.TypeName, explanation));
    }

    private static void GenerateGridFilterCode(SourceProductionContext context, GridFilterModel model)
    {
        // [Filterable(Handler = …)] reaches the model and no template reads it: the custom handler is
        // never called, and the property filters with the operator its type implies.
        foreach (var property in model.Properties)
        {
            if (string.IsNullOrEmpty(property.HandlerTypeName))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.DeclaredOptionNotHonoured,
                location: null,
                $"[Filterable(Handler = typeof({property.HandlerTypeName}))] on '{property.PropertyName}'",
                model.TypeName,
                "no template calls the handler — the property filters with the operator its type "
                + "implies. Express the custom logic as a [ComputedFilter] on the entity, or filter "
                + "with your own expression"));
        }

        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateGridBridgeCode(SourceProductionContext context, GridFilterBridgeModel model)
    {
        var template = new GridFilterBridgeTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateQueryViewCode(SourceProductionContext context, QueryViewModel model)
    {
        if (model.InertJoinCount > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.DeclaredOptionNotHonoured,
                location: null,
                "[Join<T>]",
                model.TypeName,
                "a view reads [GroupBy], [From] and the aggregate attributes, and nothing else. Reach "
                + "another entity with [GroupBy<TOther>(Via = \"Nav\")]"));
        }

        ReportCountClausesThatNameNoRow(context, model);

        foreach (var unresolved in model.UnresolvedGroupKeys)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                QueryViewDiagnostics.GroupKeyDoesNotResolve,
                unresolved.Location?.ToLocation(),
                model.TypeName,
                unresolved.Named,
                unresolved.EntityType));
        }

        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>
    ///     PRAG0722 — a <c>[Count&lt;T&gt;(Where = …)]</c> clause that never mentions the row.
    /// </summary>
    /// <remarks>
    ///     The clause goes verbatim into <c>g.Count(x =&gt; …)</c>, so one written as bare property names
    ///     — the form the attribute's own example showed — is <c>CS0103</c> inside the generated view.
    ///     Reported here, on the property, because an error in a file the author cannot open names a
    ///     symbol that <em>is</em> in scope where they wrote it.
    /// </remarks>
    private static void ReportCountClausesThatNameNoRow(SourceProductionContext context, QueryViewModel model)
    {
        foreach (var aggregate in model.AggregateProperties)
        {
            if (aggregate.Kind != Models.AggregateKind.Count)
                continue;
            if (aggregate.WhereClause is not { Length: > 0 } clause)
                continue;
            if (clause.Contains("x."))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                QueryPipelineDiagnostics.CountClauseDoesNotNameTheRow,
                aggregate.Location?.ToLocation(),
                aggregate.PropertyName,
                aggregate.EntityType,
                clause));
        }
    }

    private static void GenerateGridAdapterCode(SourceProductionContext context, GridAdapterModel model)
    {
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateFilterDtoCode(SourceProductionContext context, FilterDtoModel model)
    {
        var applyTemplate = new FilterDtoApplyTemplate(model);
        var applyArtifact = applyTemplate.RenderOutput();
        if (!applyArtifact.IsEmpty)
            context.AddSource(applyArtifact);

        var converterTemplate = new FilterDtoTypeConverterTemplate(model);
        var converterArtifact = converterTemplate.RenderOutput();
        if (!converterArtifact.IsEmpty)
            context.AddSource(converterArtifact);
    }

    private static void GeneratePatchApply(SourceProductionContext context, MutationMetadataModel? model)
    {
        if (model is null || !model.IsValid)
            return;

        ReportPatchChildProblems(context, model);

        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);

        // The producer of MarkSet: without it every patch read from a body took the "is not null"
        // fallback, where an explicit null and an absent property are the same thing.
        var converter = new PatchJsonConverterTemplate(model).RenderOutput();
        if (!converter.IsEmpty)
            context.AddSource(converter);
    }

    /// <summary>
    ///     Says out loud which children <c>ApplyPatch</c> will and will not write.
    /// </summary>
    /// <remarks>
    ///     Without it a patch that carries children the template does not write would drop them and
    ///     report nothing.
    /// </remarks>
    private static void ReportPatchChildProblems(SourceProductionContext context, MutationMetadataModel model)
    {
        foreach (var prop in model.Properties)
        {
            if (prop.IsIgnored || !prop.EntityPropertyExists || !PatchApplyTemplate.IsRelated(prop))
                continue;

            if (!prop.RelatedCanCreate && !prop.RelatedCanPatch)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PatchDiagnostics.RelatedDtoCannotWrite, model.Location,
                    model.TypeName, prop.PropertyName, prop.RelatedDtoType ?? prop.PropertyType));
                continue;
            }

            if (prop.IsCollection && prop.CollectionWrite is { KeyProblem: not CollectionKeyProblem.None })
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PatchDiagnostics.CollectionElementsCannotBeMatched, model.Location,
                    model.TypeName, prop.PropertyName, prop.ElementEntityType ?? model.EntityTypeName));
                continue;
            }

            if (prop.IsCollection && !prop.RelatedCanCreate)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PatchDiagnostics.PatchOnlyElementCannotBeAdded, model.Location,
                    model.TypeName, prop.PropertyName, prop.RelatedDtoType ?? prop.PropertyType));
            }
        }
    }
}
