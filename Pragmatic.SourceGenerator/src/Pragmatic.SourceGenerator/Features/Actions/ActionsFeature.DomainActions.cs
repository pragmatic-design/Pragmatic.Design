using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     DomainAction pipeline: diagnostics, per-action generation, registration, and metadata.
/// </summary>
internal static partial class ActionsFeature
{
    private static void ReportActionDiagnostics(SourceProductionContext context, ActionModel model)
    {
        if (model.Location is null)
            return;

        switch (model.InvalidReason)
        {
            // NotPartial: PRAG0400 is the companion analyzer's.

            case InvalidReason.NoBaseType:
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.MustInheritFromDomainAction, model.Location, model.TypeName));
                break;

            case InvalidReason.UnresolvedResultType:
                context.ReportDiagnostic(Diagnostic.Create(
                    GenerationDiagnostics.UnresolvedTypeStoppedGeneration, model.Location, model.TypeName,
                    model.UnresolvedResultType));
                break;

            case InvalidReason.Nested:
                // FullTypeName is the nested form (Outer.Inner), so the containing type is what precedes
                // the last dot — enough to name it without carrying another field on the model.
                var lastDot = model.FullTypeName.LastIndexOf('.');
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.ActionMustBeTopLevel, model.Location, model.TypeName,
                    lastDot > 0 ? model.FullTypeName.Substring(0, lastDot) : model.FullTypeName));
                break;
        }


        // PRAG0425: the declared compensator does not compensate what this action returns.
        if (model.MismatchedCompensator is { } mismatched)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.CompensatorDoesNotMatch, model.Location, model.TypeName, mismatched,
                model.IsVoid
                    ? "ICompensatesVoid"
                    : $"ICompensates<{model.ReturnTypeName ?? "TResult"}>"));
        }

        // PRAG0428: it composes, and how the steps commit is a decision nobody made.
        if (model is { ComposesInvocations: true, DeclaresCommitStrategy: false, IsComposite: false })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.UndeclaredCompositionStrategy, model.Location, model.TypeName));
        }

        // PRAG0426 first: when the action declared a transaction, the cross-boundary call is not a
        // decision left unrecorded, it is a promise that cannot be kept. Reporting both would ask the
        // author to answer a question the error has already settled.
        if (!ReportTransactionCrossesBoundary(
                context, model.TypeName, model.Location, model.IsTransactional,
                model.ForeignBoundaryFacades))
        {
            // PRAG0424: more than one boundary commits inside one invocation, and no decision recorded.
            ReportUnrecordedPartialWriteRisk(
                context, model.TypeName, model.Location, model.CommitScopeCount,
                model.ForeignBoundaryFacades, model.AcceptsPartialWrites);
        }

        // PRAG0432: a commit declaration on an action with no unit of work behind it.
        if (string.IsNullOrEmpty(model.BelongsToTypeName)
            && (model.IsTransactional || model.CommitMode is not null))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.CommitDeclarationWithoutBoundary, model.Location, model.TypeName,
                model.IsTransactional ? "Transactional" : "CommitStrategy"));
        }

        // PRAG0433: an event parameter that binds to nothing and is dispatched as default.
        foreach (var raised in model.RaisedEvents)
        {
            foreach (var parameter in raised.UnmatchedParameters)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.EventParameterHasNoSource, model.Location,
                    model.TypeName, raised.EventFullName, parameter, string.Empty));
            }
        }

        // PRAG0429: a cross-boundary step that undoes itself — accepted, and named for what it is.
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

        // PRAG0423: the delegation subject does not resolve, so no scope was generated. Without this
        // the action runs as the caller while its declaration says it acts for someone else.
        if (model.BadDelegationSubject is { } badSubject)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.DelegationSubjectNotFound, model.Location, model.TypeName, badSubject));
        }

        ReportLoadEntityDiagnostics(context, model.LoadEntityDiagnostics, model.Location, model.TypeName);
    }

    /// <summary>
    ///     The <c>[LoadEntity]</c> and <c>[LoadEntities]</c> declarations that could not be generated — on an
    ///     action or a mutation.
    /// </summary>
    private static void ReportLoadEntityDiagnostics(
        SourceProductionContext context, EquatableArray<LoadEntityDiagnosticInfo> diagnostics,
        Location? location, string typeName)
    {
        foreach (var diag in diagnostics)
        {
            switch (diag.Kind)
            {
                case LoadEntityDiagnosticKind.IdPropertyNotFound:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadEntityIdPropertyNotFound, location,
                        diag.EntityTypeName, typeName, diag.IdPropertyName, diag.AttributeName));
                    break;

                case LoadEntityDiagnosticKind.KeyTypeNotFound:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadEntityKeyTypeNotFound, location,
                        diag.EntityTypeName, typeName, diag.AttributeName));
                    break;

                case LoadEntityDiagnosticKind.IncludeNotANavigation:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadEntityIncludeNotANavigation, location,
                        diag.EntityTypeName, typeName, diag.IncludePath, diag.IncludeSegment,
                        diag.AttributeName == "EagerLoad" ? "EagerLoad" : $"{diag.AttributeName}<{diag.EntityTypeName}>(Include)"));
                    break;

                case LoadEntityDiagnosticKind.SpecificationNotFound:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadSpecificationNotFound, location,
                        diag.EntityTypeName, typeName, diag.AttributeName, diag.SpecificationName, diag.Reason));
                    break;

                case LoadEntityDiagnosticKind.SpecificationParameterUnbound:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadSpecificationParameterUnbound, location,
                        diag.EntityTypeName, typeName, diag.AttributeName, diag.Reason));
                    break;

                case LoadEntityDiagnosticKind.LoadFromInputUnbound:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadFromInputUnbound, location,
                        diag.EntityTypeName, typeName, diag.AttributeName, diag.Reason));
                    break;

                case LoadEntityDiagnosticKind.KeyOrSpecification:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadEntityKeyOrSpecification, location,
                        diag.EntityTypeName, typeName, diag.AttributeName, diag.Reason));
                    break;

                case LoadEntityDiagnosticKind.ByNotTheLogicKey:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadByNotTheLogicKey, location,
                        diag.EntityTypeName, typeName, diag.AttributeName, diag.Reason));
                    break;

                case LoadEntityDiagnosticKind.ExistsBesideALoad:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.RequireExistsBesideALoad, location,
                        diag.EntityTypeName, typeName, diag.IdPropertyName));
                    break;

                case LoadEntityDiagnosticKind.ByKeyTypeMismatch:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadByKeyTypeMismatch, location,
                        diag.EntityTypeName, typeName, diag.AttributeName, diag.LogicKeyMember, diag.EntityKeyType,
                        diag.IdPropertyName, diag.SuppliedKeyType));
                    break;

                case LoadEntityDiagnosticKind.KeyTypeMismatch:
                    context.ReportDiagnostic(Diagnostic.Create(
                        ActionsDiagnostics.LoadEntityKeyTypeMismatch, location,
                        diag.EntityTypeName, typeName, diag.IdPropertyName, diag.SuppliedKeyType, diag.EntityKeyType,
                        diag.AttributeName, diag.KeyAdvice));
                    break;
            }
        }
    }

    /// <summary>
    ///     <c>PRAG0457</c>: a load that asks the read permission of an entity the permission catalogue has
    ///     none for — reported rather than generated as a check that asks nothing.
    /// </summary>
    private static void ReportLoadReadPermissions(
        SourceProductionContext context, EquatableArray<LoadEntityModel> loads, Location? location, string typeName)
    {
        foreach (var load in loads)
        {
            if (!load.RequireReadPermission || load.ReadPermission is not null)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.LoadReadPermissionUnknown, location,
                load.EntityTypeShortName, typeName, load.IsMany ? "LoadEntities" : "LoadEntity"));
        }
    }

    /// <summary><c>PRAG0458</c>: a <c>[LoadFrom]</c> property the declared query cannot fill.</summary>
    private static void ReportLoadFromQueries(
        SourceProductionContext context, EquatableArray<LoadFromQueryModel> loads, Location? location, string typeName)
    {
        foreach (var load in loads)
        {
            if (load.ResultProblem is null)
                continue;

            var query = load.QueryTypeFullName.Substring(load.QueryTypeFullName.LastIndexOf('.') + 1);
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.LoadFromNotTheQuerysAnswer, location,
                query, typeName, load.PropertyName, load.ResultProblem));
        }
    }

    private static void GenerateSetDependencies(SourceProductionContext context, ActionModel model)
    {
        if (!model.HasDependencies)
            return;

        var artifact = new SetDependenciesTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }

    private static void GenerateLoadEntity(SourceProductionContext context, ActionModel model)
    {
        GenerateCurrentUserField(context, model.TypeName, model.Namespace, model.Accessibility, model.Bindings);

        if (!model.HasLoadEntities)
            return;

        var artifact = new LoadEntityTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }

    private static void GenerateInvoker(SourceProductionContext context, ActionModel model)
    {
        var artifact = new InvokerTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }

    private static void GenerateVersioning(SourceProductionContext context, ActionModel model)
    {
        if (!model.HasVersioning)
            return;

        var artifact = new VersioningTemplate(model).RenderOutput();
        context.AddSource(artifact);
    }

    private static void GenerateValidationMetadata(
        SourceProductionContext context, ActionModel model, EquatableArray<string> asyncValidatedTypes)
    {
        // Without [Validate], a [Validator] in this compilation is the opt-in — for the action itself,
        // or for a nested type it validates — and only the nested properties that have one are asked
        // for it. [Validate] keeps its meaning: it decides, and its nested set is every validatable one.
        var validated = asyncValidatedTypes.AsImmutableArray();
        var asyncNested = model.ValidateIsDeclared
            ? model.AsyncNestedProperties.AsImmutableArray()
            : model.SyncNestedProperties
                .Where(p => validated.Contains(p.PropertyTypeName))
                .Select(p => new AsyncNestedValidatorProperty(p.PropertyName, p.PropertyTypeName, p.IsCollection))
                .ToImmutableArray();
        var runAsync = model.ValidateIsDeclared
            ? model.RunAsync
            : validated.Contains(model.FullTypeName) || !asyncNested.IsEmpty;

        // Only generate when there's non-default validation config
        // Default: HasNoValidation=false, RunSync=true, RunAsync=false, no nested props
        // Skip generation for default case → avoids referencing Pragmatic.Validation in projects without it
        if (!model.HasNoValidation && model.RunSync && !runAsync
            && model.SyncNestedProperties.IsDefaultOrEmpty)
            return;

        var validationModel = new ActionValidationModel
        {
            TypeName = model.TypeName,
            FullTypeName = model.FullTypeName,
            Namespace = model.Namespace,
            Accessibility = model.Accessibility,
            HasNoValidation = model.HasNoValidation,
            RunSync = model.RunSync,
            RunAsync = runAsync,
            SyncNestedProperties = model.SyncNestedProperties,
            AsyncNestedProperties = asyncNested
        };

        var artifact = new ValidationMetadataTemplate(validationModel).RenderOutput();
        context.AddSource(artifact);
    }

    private static void GenerateActionsMetadata(
        SourceProductionContext context,
        ((ImmutableArray<ActionModel> Actions, ImmutableArray<MutationModel> Mutations) Left,
            (bool IsDebug, bool HasComposition) Info) input)
    {
        var ((actions, mutations), (isDebug, hasComposition)) = input;

        if (!hasComposition || (actions.IsEmpty && mutations.IsEmpty))
            return;

        var artifact = new ActionsMetadataTemplate(actions, mutations, isDebug).RenderOutput();
        context.AddSource(artifact);
    }

    /// <summary>
    ///     The same document <see cref="GenerateActionsMetadata" /> writes, handed to Composition for a
    ///     host that declares its own <c>[DomainAction]</c> / <c>[Mutation]</c> types.
    /// </summary>
    /// <remarks>
    ///     Mirrors the generation condition exactly — the host must see these entries in the shape that
    ///     declaring them in a library produces, and in no other. That now includes the two registration
    ///     methods: since the host calls a referenced module's own <c>Add{Prefix}Actions</c> /
    ///     <c>Add{Prefix}Mutations</c> instead of writing the invoker lines again, a host that declares
    ///     the same types itself has to be wired by the same call, or the two shapes differ.
    /// </remarks>
    private static EquatableArray<Composition.Models.MetadataEntry> LocalActionsRegistrations(
        ((ImmutableArray<ActionModel> Actions, ImmutableArray<MutationModel> Mutations) Left,
            (bool IsDebug, bool HasComposition) Info) input,
        string assemblyName)
    {
        var ((actions, mutations), (isDebug, hasComposition)) = input;

        if (!hasComposition || (actions.IsEmpty && mutations.IsEmpty))
            return EquatableArray<Composition.Models.MetadataEntry>.Empty;

        var builder = ImmutableArray.CreateBuilder<Composition.Models.MetadataEntry>();
        builder.Add(Composition.Models.HostLocalRegistration.CreatePayload(
            MetadataCategoryIds.Actions,
            MetadataSchemaVersions.Actions,
            new ActionsMetadataTemplate(actions, mutations, isDebug).BuildJson(),
            Templates.ActionsRegistrationTemplate.RegistrationMethodFor(actions) ?? "",
            Templates.MutationRegistrationTemplate.RegistrationMethodFor(mutations) ?? ""));

        // The derived-permission catalog's own registration is produced by
        // RegisterDerivedPermissionCatalog, beside the catalog, once the reads are counted too.
        return builder.ToImmutable();
    }

    private static void GenerateActionsRegistration(
        SourceProductionContext context,
        (ImmutableArray<ActionModel> Actions, ImmutableArray<MutationModel> Mutations) pair)
    {
        var actions = pair.Actions;
        if (actions.IsEmpty)
            return;

        // Register the generated authorization registries from the actions registration
        // (the assembly has actions, so this is the canonical place — the mutations registration
        // defers to avoid a duplicate).
        var (permNs, policyNs) = ComputeAuthorizationRegistryNamespaces(actions, pair.Mutations);

        var template = new ActionsRegistrationTemplate(actions, permNs, policyNs);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }
}
