using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     Mutation and CompositeAction pipelines: diagnostics, per-mutation generation, and registration.
/// </summary>
internal static partial class ActionsFeature
{
    private static void ReportMutationDiagnostics(SourceProductionContext context, MutationModel model)
    {
        // PRAG0463: a rule the invoker cannot call. ⚠️ Reported before the `model.Location is null`
        // return below, and on the METHOD's own location: the author has to be sent to the invariant,
        // and a mutation whose own location the transform could not resolve still has an entity whose
        // rule is enforced nowhere.
        foreach (var uncallable in model.UncallableInvariants)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.InvariantCannotBeCalled,
                uncallable.Location?.ToLocation(),
                uncallable.MethodName, uncallable.Reason));
        }

        if (model.Location is null)
            return;

        // PRAG0435: nothing to load the row by. Reported before the validity switch because the model
        // is otherwise perfectly valid — that is the whole problem: it compiles and finds nothing.
        if (model.IdProblem != MutationIdProblem.None)
        {
            var complaint = model.IdProblem == MutationIdProblem.Missing
                ? "it declares no Id property"
                : $"its Id is not a {ShortName(model.EntityIdTypeName)}";

            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.MutationIdPropertyUnusable, model.Location,
                model.TypeName, model.Mode, model.EntityTypeName, complaint,
                ShortName(model.EntityIdTypeName)));
        }

        if (!model.IsValid)
        {
            switch (model.InvalidReason)
            {
                // NotPartial: PRAG0400 is the companion analyzer's.

                case MutationInvalidReason.NoBaseType:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.MutationMustInheritFromBase, model.Location, model.TypeName));
                    break;

                case MutationInvalidReason.ModeNotDetermined:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.MutationModeNotDetermined, model.Location, model.TypeName));
                    break;
            }
            return;
        }

        ReportLoadEntityDiagnostics(context, model.LoadEntityDiagnostics, model.Location, model.TypeName);

        // PRAG0703: an option declared on a write that only a read can honour. Reported here because
        // this is the pipeline that would have to read it, and the one that knows it does not — every
        // other PRAG0703 is emitted from QueryFeature, which never sees a mutation.
        if (model.HasInertQueryStrategy)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Persistence.Diagnostics.QueryPipelineDiagnostics.DeclaredOptionNotHonoured,
                model.Location,
                "[QueryStrategy]",
                model.TypeName,
                "a write loads its row tracked, because it is about to change it, and no template reads "
                + "the strategy on this path. What a write declares instead is [FilterMode] for the "
                + "ladder, and [WithoutFilter<T>] to lift one named filter"));
        }

        // PRAG0425: the declared compensator does not compensate the entity this mutation writes.
        if (model.MismatchedCompensator is { } mismatched)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.CompensatorDoesNotMatch, model.Location, model.TypeName, mismatched,
                $"ICompensates<{model.EntityFullTypeName ?? "TEntity"}>"));
        }

        // PRAG0433: an event parameter that binds to nothing and is dispatched as default. For a
        // mutation the entity's properties are candidates too, so the message says so.
        foreach (var raised in model.RaisedEvents)
        {
            foreach (var parameter in raised.UnmatchedParameters)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.EventParameterHasNoSource, model.Location,
                    model.TypeName, raised.EventFullName, parameter, " or entity property"));
            }
        }

        // PRAG0424: more than one boundary commits inside one invocation, and no decision recorded.
        ReportUnrecordedPartialWriteRisk(
            context, model.TypeName, model.Location, model.CommitScopeCount,
            model.ForeignBoundaryFacades, model.AcceptsPartialWrites);

        ReportCompensationIsNotDurable(
            context, model.TypeName, model.Location, model.CompensatedForeignSteps);

        // PRAG0419 / PRAG0420: silent misreadings made loud.
        foreach (var ambiguous in model.AmbiguousDependencies)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.DependencyTypeAmbiguous, model.Location,
                ambiguous.FieldName, model.TypeName, ambiguous.TypeName));
        }

        // PRAG0448: a DbContext or an IUnitOfWork declared where no boundary answers. Those two are
        // registered keyed by boundary, so without one the generated invoker asks for them unkeyed and
        // the application does not start — a container error naming a generated type, and neither the
        // field nor the fix mentioned. Same shape as PRAG0447 for a composite.
        foreach (var dep in model.Dependencies)
        {
            if (model.AssemblyDeclaresABoundary
                && Transforms.BoundaryKeyedServices.IsKeyedByBoundary(dep.TypeName)
                && string.IsNullOrEmpty(dep.KeyedServiceType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.KeyedServiceWithoutBoundary, model.Location,
                    model.TypeName, dep.TypeName));
            }
        }

        if (model.HasBlankResiliencePolicy)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.ResiliencePolicyNameEmpty, model.Location, model.TypeName));
        }

        ReportTransitionProblem(context, model.TypeName, model.Location, model.Transition);

        // PRAG0414: Mutation properties that have no matching setter on the entity
        foreach (var unmapped in model.UnmappedProperties)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.MutationPropertyNoMatchingSetter,
                model.Location,
                unmapped.PropertyName,
                unmapped.EntityTypeName));
        }

        // PRAG0445: a declared target nobody reads. Where Mapping is referenced it reads it and the
        // property is written; where it is not, the attribute compiles and does nothing — silence exactly
        // where the author was most explicit.
        if (!model.MappingOwnsTheBody)
            foreach (var retargeted in model.RetargetedProperties)
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.MutationRetargetNeedsMapping,
                    model.Location,
                    retargeted,
                    model.TypeName));

        // PRAG0446: a required constructor parameter no property matches. Without it the invoker would
        // build the entity with `default` in its place, silently.
        foreach (var parameter in model.ConstructorParameters)
            if (parameter is { MatchingPropertyName: null, IsOptional: false })
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.MutationMissesAConstructorArgument,
                    model.Location,
                    model.TypeName, model.EntityTypeName, parameter.Name));

        ReportChildProblems(context, model);

        // PRAG0434: the mapping would assign a state the entity governs with transitions.
        if (model.StateMachineMappedProperty is { } governed)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.MutationAssignsStateMachineProperty,
                model.Location,
                model.TypeName,
                governed,
                model.EntityTypeName));
        }

        // PRAG0403: a LogicalKey the mutation cannot type. Generated as returning the entity meanwhile,
        // so this is the error the build stops on.
        if (model.LogicalKeyProblem is { } problem)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.LogicalKeyCannotBeReturned,
                model.Location,
                model.TypeName,
                problem));
        }
    }

    /// <summary>The record a <c>ReturnType = LogicalKey</c> mutation returns.</summary>
    private static void GenerateMutationLogicalKey(SourceProductionContext context, MutationModel model)
    {
        var artifact = new MutationLogicalKeyTemplate(model).RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateMutationSetDependencies(SourceProductionContext context, MutationModel model)
    {
        if (!model.HasDependencies)
            return;

        var artifact = new MutationSetDependenciesTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }

    private static void GenerateMutationLoadEntity(SourceProductionContext context, MutationModel model)
    {
        GenerateCurrentUserField(context, model.TypeName, model.Namespace, model.Accessibility, model.Bindings);

        if (!model.HasLoadEntities)
            return;

        var artifact = new LoadEntityTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }

    /// <summary>
    ///     The <c>Id</c> a load-mode mutation is addressed by, when the author declared none.
    /// </summary>
    private static void GenerateMutationId(SourceProductionContext context, MutationModel model)
    {
        var artifact = new MutationIdTemplate(model).RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    private static void GenerateMutationAutoMap(SourceProductionContext context, MutationModel model)
    {
        // Always generate ApplyToEntity when there are mapped properties, even with manual ApplyAsync:
        // the invoker calls it before ApplyAsync, so an override customises rather than replaces.
        // It is NOT reached through base.ApplyAsync(), which only returns the entity — this comment said
        // otherwise, and following it would have produced a mutation that maps nothing.
        // Children are mapped too, and asking only about scalars meant an operation whose entire
        // payload was a collection generated nothing at all: it compiled, the endpoint answered 200,
        // and the set the caller sent came back unchanged. The template's own Validate already knew.
        if (!model.WritesSomething)
            return;

        var artifact = new MutationAutoMapTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }

    private static void GenerateMutationInvoker(SourceProductionContext context, MutationModel model)
    {
        var artifact = new MutationInvokerTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }

    private static void GenerateMutationsRegistration(
        SourceProductionContext context,
        (ImmutableArray<ActionModel> Actions, ImmutableArray<MutationModel> Mutations) pair)
    {
        var mutations = pair.Mutations;
        if (mutations.IsEmpty)
            return;

        // Register the generated authorization registries here ONLY when the assembly has
        // no actions (otherwise GenerateActionsRegistration already does, avoiding a duplicate).
        string? permNs = null, policyNs = null;
        if (pair.Actions.IsEmpty)
            (permNs, policyNs) = ComputeAuthorizationRegistryNamespaces(pair.Actions, mutations);

        var template = new MutationRegistrationTemplate(mutations, permNs, policyNs);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>
    ///     The composite invoker, with the boundary its unit of work is keyed by.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The boundary is <b>derived</b> where the author did not write it, using the same
    ///         namespace match every other member of a boundary already goes through. A mutation does
    ///         not need this — it reads the boundary off its entity — but a composite has no entity.
    ///         Without the derivation <c>[BelongsTo]</c> would be silently mandatory on a composite:
    ///         the generated constructor would ask for an unkeyed <c>IUnitOfWork</c>, which nothing
    ///         registers, and the application would fail to start on a container validation error
    ///         naming a generated type.
    ///     </para>
    ///     <para>
    ///         Where the namespace answers for nobody, <c>PRAG0447</c> says so and the constructor stays
    ///         unkeyed: an application that registers its own unkeyed unit of work keeps working, and
    ///         one that does not learns why before it runs.
    ///     </para>
    /// </remarks>
    private static void GenerateCompositeInvoker(
        SourceProductionContext context,
        CompositeActionModel model,
        ImmutableArray<BoundaryModel> boundaries)
    {
        var resolved = model;

        if (string.IsNullOrEmpty(model.BelongsToTypeName))
        {
            var match = FindBestBoundaryMatch(model.Namespace, boundaries);

            if (match is not null)
                resolved = model with { BelongsToTypeName = match.FullTypeName };
            else
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.CompositeWithoutBoundary, model.Location, model.TypeName));
        }

        var artifact = new CompositeActionInvokerTemplate(resolved).RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>
    ///     Says which children the mutation will not write, and why.
    /// </summary>
    /// <remarks>
    ///     Fail-closed on purpose. A parent that writes a child writes a row past the permissions,
    ///     validation and events the child's own operations would have applied, and no amount of
    ///     relation metadata separates a line item from a room type — both are declared
    ///     <c>[Relation.OneToMany]</c>. Silence here would make the parent a way around the child's
    ///     permission, which is the shape this codebase has already shipped twice.
    /// </remarks>
    private static void ReportChildProblems(SourceProductionContext context, MutationModel model)
    {
        // PRAG0438: the entity says it is written through a parent, and this mutation is EXPOSED as an
        // operation of its own.
        //
        // `IsInternal` is the discriminator, and it means "no endpoint". A child mutation — the shape a
        // parent nests — is internal by construction: it is reached only through its parent, so it is
        // not the contradiction this diagnostic exists to catch. The concern in the remark above was
        // that a parent writes a child "past the permissions, validation and events the child's own
        // operations would have applied"; a child that IS a mutation applies exactly those, which is
        // why the rule and this diagnostic can now both hold.
        if (model is { TargetsAChildAggregate: { } declaredParent, IsInternal: false, ChildAggregateIsExclusive: true })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.MutationTargetsAChildOfAnAggregate, model.Location,
                model.TypeName, model.EntityTypeName, declaredParent));
        }

        foreach (var child in model.Children)
        {
            // PRAG0444: the write would cross a boundary, which is the transaction boundary. Both
            // names have to be known — an entity that declares no boundary is not crossing one.
            if (child.ParentBoundaryName is { Length: > 0 } parentBoundary
                && child.ChildBoundaryName is { Length: > 0 } childBoundary
                && !string.Equals(parentBoundary, childBoundary, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.MutationChildCrossesABoundary, model.Location,
                    model.TypeName, child.PropertyName, child.ChildTypeName, childBoundary,
                    model.EntityTypeName, parentBoundary));
            }

            // PRAG0442: a child of an aggregate is written through a mutation, never through a DTO.
            if (child is { IsChildMutation: false, EntityPropertyExists: true })
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.MutationCarriesADtoChild, model.Location,
                    model.TypeName, child.PropertyName, child.ChildDtoTypeName, child.ChildTypeName));
            }

            // PRAG0443: the child writes one entity and the navigation holds another. Reported only
            // when the child is otherwise allowed here, so the more specific cause speaks first.
            if (child is { EntityPropertyExists: true, IsPartOfThisAggregate: true, MatchesTheNavigation: false })
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.MutationChildDoesNotMatchTheNavigation, model.Location,
                    model.TypeName, child.PropertyName, child.ChildTypeName,
                    child.TargetPropertyName, model.EntityTypeName, child.NavigationElementTypeName));
            }

            // PRAG0439: nowhere to put it. Skipping quietly is what made a curated set come back
            // unchanged from an endpoint that had answered 200.
            if (!child.EntityPropertyExists)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.MutationChildHasNoNavigation, model.Location,
                    model.TypeName, child.PropertyName, model.EntityTypeName, child.ChildTypeName));
                continue;
            }

            // No branch for «neither creatable nor patchable»: it is unreachable. To get here the child
            // must be a mutation — a DTO is caught by PRAG0442, a missing navigation by PRAG0439 — and a
            // mutation always has CanCreate, because it is built and filled as the invoker does with the
            // root. A `continue` here would skip a child silently.

            // PRAG0436: the child never said it has no life of its own.
            if (!child.IsPartOfThisAggregate)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.MutationChildIsNotPartOfTheAggregate, model.Location,
                    model.TypeName, child.PropertyName, child.ChildTypeName, child.ParentTypeName));
                continue;
            }

            // PRAG0437: nothing to match the incoming elements against the ones already there.
            if (!child.ElementsCanBeMatched)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.MutationChildElementsCannotBeMatched, model.Location,
                    model.TypeName, child.PropertyName, child.ChildTypeName));
            }
        }
    }

    /// <summary>The type name without its namespace — a diagnostic message is read, not compiled.</summary>
    private static string ShortName(string? fullTypeName)
    {
        if (string.IsNullOrEmpty(fullTypeName))
            return "id";

        var name = fullTypeName!.StartsWith("global::", StringComparison.Ordinal)
            ? fullTypeName.Substring("global::".Length)
            : fullTypeName;
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name.Substring(dot + 1);
    }
}
