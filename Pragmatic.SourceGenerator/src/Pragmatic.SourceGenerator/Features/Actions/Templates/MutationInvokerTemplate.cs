using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Generates a concrete MutationInvoker subclass and a per-mutation DI extension method.
///     The invoker implements LoadEntityAsync, CreateEntity, GetMode, PersistNew, SaveChangesAsync
///     by delegating to IRepository and IUnitOfWork.
/// </summary>
internal sealed partial class MutationInvokerTemplate : CSharpTemplate
{
    private readonly MutationModel _model;

    public MutationInvokerTemplate(MutationModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} Mutation from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Mutation] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "MutationInvoker", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        if (NeedsInfraCurrentUser)
            AddUsing("Pragmatic.Identity");

        AppendNamespace(_model.Namespace);
        AppendLine();

        var baseClass = GetBaseClass();

        // Nested inside partial mutation class (1 nested class per file)
        Class(_model.TypeName, () =>
        {
            XmlSummary(
                $"Generated mutation invoker for <see cref=\"{_model.TypeName}\"/>. " +
                "Orchestrates the pipeline: validate -> load/create -> apply -> entity validate -> persist -> events.");

            Class("Invoker", RenderInvokerBody,
                baseType: baseClass,
                accessModifier: InvokerAccessibility(),
                modifiers: new ClassModifiers { Sealed = true });
        },
        accessModifier: ParseAccessibility(_model.Accessibility),
        modifiers: new ClassModifiers { Partial = true });
    }

    /// <summary>An invoker is no more accessible than the least accessible thing it takes.</summary>
    /// <remarks>
    ///     The same rule as the action invoker, and it applies here for the same reason: a mutation
    ///     injecting its module's <c>internal</c> boundary facade produced <c>CS0051</c> in a file the
    ///     consumer cannot edit. The action side had a workaround; this side had nothing.
    /// </remarks>
    private AccessModifier InvokerAccessibility()
        => _model.Dependencies.Any(d => d.IsInternalType)
            ? AccessModifier.Internal
            : AccessModifier.Public;

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            _ => AccessModifier.Public
        };
    }

    /// <summary>
    ///     Whether ICurrentUser is already injected as a mutation dependency,
    ///     preventing duplicate _currentUser field generation.
    /// </summary>
    private bool HasCurrentUserDependency =>
        _model.Dependencies.Any(d => d.FieldName == "_currentUser");

    /// <summary>
    ///     Whether we need _currentUser for infrastructure (SoftDelete/Ownership)
    ///     AND it's not already provided as a dependency.
    /// </summary>
    private bool NeedsInfraCurrentUser =>
        (_model.HasSoftDelete || _model.IsOwnedEntity) && !HasCurrentUserDependency;

    /// <summary>
    ///     Whether this invoker stamps an instant of its own, and therefore needs a clock.
    /// </summary>
    /// <remarks>
    ///     Only the soft-delete path writes a time here; the rest of a row's timestamps are the
    ///     interceptors' business. <c>DateTimeOffset.UtcNow</c> written straight into the generated
    ///     code is something no container can replace: an application that pinned its clock would get
    ///     a row whose <c>CreatedAt</c> obeyed and whose <c>DeletedAt</c> did not.
    /// </remarks>
    private bool NeedsClock => _model.HasSoftDelete;

    private void RenderInvokerBody()
    {
        // Fields: repository + unit of work + current user (soft delete/ownership) + mutation dependencies
        if (_model.EntityIdTypeName is not null)
            Field("_repository", RepositoryTypeName, AccessModifier.Private, isReadOnly: true);

        Field("_unitOfWork", UnitOfWorkTypeName, AccessModifier.Private, isReadOnly: true);

        if (NeedsInfraCurrentUser)
            Field("_currentUser", "global::Pragmatic.Identity.ICurrentUser?", AccessModifier.Private, isReadOnly: true);

        if (NeedsClock)
            Field("_timeProvider", "global::System.TimeProvider", AccessModifier.Private, isReadOnly: true);

        if (_model.IsRestore || _model.HasFilterOverrides)
            Field("_filterToggle", "global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle?", AccessModifier.Private, isReadOnly: true);

        foreach (var dep in _model.Dependencies)
            Field(TemplateHelpers.InvokerFieldName(dep.FieldName), dep.TypeName, AccessModifier.Private, isReadOnly: true);

        if (_model.HasResilience)
            Field("_resiliencePipelineProvider", ResilienceProviderTypeName, AccessModifier.Private, isReadOnly: true);

        AppendLine();

        RenderConstructor();
        AppendLine();
        RenderInjectDependencies();
        AppendLine();
        RenderLoadEntityAsync();
        AppendLine();
        RenderPrepareMutationAsync();
        RenderLinkChosenRows();
        RenderCreateEntity();
        RenderNestedOperations();
        RenderValidateNestedTree();
        RenderEnsureWrittenGraphLoaded();
        AppendLine();
        RenderGetMode();
        AppendLine();
        RenderGetEntityIdString();
        AppendLine();
        RenderHasEntityId();
        AppendLine();
        RenderPersistNew();
        AppendLine();
        RenderDeleteEntity();
        AppendLine();

        if (_model.IsRestore)
        {
            RenderRestoreEntity();
            AppendLine();
        }

        if (_model is { HasSoftDelete: true, SoftDelete.HasCascadeTargets: true })
        {
            RenderCompensateSoftDeleteCascade();
            AppendLine();
        }

        RenderSaveChangesAsync();

        AppendLine();
        AppendLine(
            "protected override global::Pragmatic.Persistence.Repository.IUnitOfWork? UnitOfWork => _unitOfWork;");

        if (_model.CommitMode is not null)
        {
            AppendLine();
            AppendLine(
                $"protected override global::Pragmatic.Actions.Commit.CommitMode CommitMode => global::Pragmatic.Actions.Commit.CommitMode.{_model.CommitMode};");
        }

        if (_model.HasComputedDefaults)
        {
            AppendLine();
            RenderApplyComputedDefaultsAsync();
        }

        if (_model.HasPresets)
        {
            AppendLine();
            RenderApplyPresetsAsync();
        }

        if (_model.HasRaisedEvents)
        {
            AppendLine();
            RenderCollectRaisedEvents();
        }

        if (_model.HasInvariants)
        {
            AppendLine();
            RenderCheckInvariants();
        }

        if (_model.HasTemporalConstraints)
        {
            AppendLine();
            RenderCheckTemporalConstraints();
        }

        if (_model.CompensatorTypeName is not null)
        {
            AppendLine();
            RenderCompensation();
        }

        // [AbsorbsChildPermissions]: what the body invokes runs as an internal call. On an action the
        // attribute already meant this; on a mutation it covered only the nested children, so the same
        // attribute meant two things and a mutation crossing a boundary could not declare it at all.
        if (_model.AbsorbsChildPermissions)
        {
            AppendLine();
            XmlSummary("This mutation answers for the permissions of what it invokes.");
            AppendLine("protected override bool AbsorbsChildPermissions => true;");
        }

        if (_model.HasResilience)
        {
            AppendLine();
            RenderRunAttemptsUnderThePolicy();
        }

        RenderTransition();

    }

    /// <summary>
    ///     The undo declared by <c>[UndoWith&lt;T&gt;]</c>, committed through this invoker's own unit of
    ///     work so it lands in the boundary that owns the entity.
    /// </summary>
    private void RenderCompensation()
    {
        AppendLine("protected override bool IsCompensable => true;");
        AppendLine();

        XmlSummary("Runs the declared compensator, then commits it through this invoker's unit of work.");

        AppendLine(
            "protected override async global::System.Threading.Tasks.Task<global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>> " +
            $"CompensateAsync({_model.EntityFullTypeName} committed, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            AppendLine(
                $"var compensator = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{_model.CompensatorTypeName}>(_serviceProvider);");
            AppendLine("var undone = await compensator.Undo(committed, ct).ConfigureAwait(false);");
            AppendLine("if (undone.IsFailure)");
            AppendLine("    return undone;");
            AppendLine();
            Comment("The undo staged its writes; without this commit it would be a no-op that reports success.");
            AppendLine("await SaveChangesAsync(ct).ConfigureAwait(false);");
            AppendLine("return global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>.Success();");
        });
    }

    /// <summary>Generates the CheckInvariants override calling each [Invariant] bool method on the entity.</summary>
    private void RenderCheckInvariants()
    {
        XmlSummary("Checks the entity's [Invariant] methods after apply, before persist; first violation rejects the mutation.");
        AppendLine(
            "protected override global::Pragmatic.Result.IError? " +
            $"CheckInvariants({_model.EntityFullTypeName} entity)");
        Block(() =>
        {
            foreach (var invariant in _model.Invariants)
            {
                AppendLine($"if (!entity.{invariant.MethodName}())");
                IncreaseIndent();
                AppendLine(Refusal(invariant.MethodName, invariant));
                DecreaseIndent();
            }

            foreach (var child in _model.ChildrenWithInvariants)
            {
                AppendLine();
                RenderChildInvariants(child);
            }

            AppendLine("return null;");
        });
    }

    /// <summary>
    ///     Checks the rules of a child this mutation merged.
    /// </summary>
    /// <remarks>
    ///     The aggregate answers for its parts. A <c>[PartOf]</c> child is written through its parent and
    ///     has no operations of its own, so without this an <c>[Invariant]</c> on it is checked on no
    ///     path at all — measured on an invoice line at an unknown VAT rate, created with a 201. The
    ///     invoker knows which children to ask because it is the code that merged them.
    /// </remarks>
    private void RenderChildInvariants(Models.MutationChildModel child)
    {
        Comment($"{child.ChildTypeName} is part of this aggregate: its rules are checked here, "
                + "because nothing else writes it.");

        if (child.IsCollection)
        {
            // `is not null` on a navigation that should never be: a collection nobody initialised is
            // not a reason to throw from inside a generated file.
            AppendLine($"if (entity.{child.TargetPropertyName} is not null)");
            Block(() =>
            {
                AppendLine($"foreach (var __child in entity.{child.TargetPropertyName})");
                Block(() => RenderChildRules(child, "__child"));
            });
            return;
        }

        AppendLine($"if (entity.{child.TargetPropertyName} is not null)");
        Block(() => RenderChildRules(child, $"entity.{child.TargetPropertyName}"));
    }

    /// <summary>Each of the child's rules, asked of <paramref name="instance" />.</summary>
    private void RenderChildRules(Models.MutationChildModel child, string instance)
    {
        foreach (var invariant in child.Invariants)
        {
            AppendLine($"if (!{instance}.{invariant.MethodName}())");
            IncreaseIndent();
            // Named with the child, because the aggregate and its parts may both carry a rule of that
            // name and the refusal has to say which one refused.
            AppendLine(Refusal($"{child.ChildTypeName}.{invariant.MethodName}", invariant));
            DecreaseIndent();
        }
    }

    /// <summary>
    ///     The refusal of one rule: which rule, its sentence, and — when the rule names one — the
    ///     translation key the sentence lives under.
    /// </summary>
    /// <remarks>
    ///     The key is what makes the refusal readable in the caller's language: without it the error
    ///     reports <c>error.invariant.violation</c>, which every invariant in the application shares.
    ///     A rule that names none is rendered exactly as before, with two arguments.
    /// </remarks>
    private static string Refusal(string name, Models.InvariantModel invariant)
    {
        var message = StringHelper.CSharpLiteral(invariant.Message ?? "");
        var key = invariant.MessageKey is { Length: > 0 } declared
            ? $", \"{StringHelper.CSharpLiteral(declared)}\""
            : "";

        return "return new global::Pragmatic.Actions.Mutation.InvariantViolationError("
               + $"\"{name}\", \"{message}\"{key});";
    }

    /// <summary>Generates the CheckTemporalConstraints override enforcing [TemporalRelation] MaxActive/overlap.</summary>
    private void RenderCheckTemporalConstraints()
    {
        XmlSummary("Enforces the entity's [TemporalRelation] MaxActive/overlap constraints on create by calling its generated ValidateTemporalConstraints against existing records; a breach rejects the mutation (409).");
        AppendLine(
            "protected override global::Pragmatic.Result.IError? " +
            $"CheckTemporalConstraints({_model.EntityFullTypeName} entity)");
        Block(() =>
            AppendLine("return entity.ValidateTemporalConstraints(_repository.Query());"));
    }

    /// <summary>Builds the mutation's <c>[Raises&lt;T&gt;]</c> events (ctor filled by name); dispatched by the pipeline.</summary>
    private void RenderCollectRaisedEvents()
    {
        XmlSummary("Builds the domain events declared via [Raises&lt;T&gt;], filled by name from the mutation and entity.");
        var parts = _model.RaisedEvents
            .Select(ev => $"new {ev.EventFullName}({string.Join(", ", ev.ConstructorArguments)})");
        AppendLine(
            "protected override global::System.Collections.Generic.IReadOnlyList<global::Pragmatic.Events.IDomainEvent> " +
            $"CollectRaisedEvents({_model.FullTypeName} mutation, {_model.EntityFullTypeName} entity)");
        AppendLine($"    => [{string.Join(", ", parts)}];");
    }

    private void RenderConstructor()
    {
        var ctorParams = new List<string>();

        if (_model.EntityIdTypeName is not null)
            ctorParams.Add($"{RepositoryTypeName} repository");

        var uowParam = _model.BelongsToTypeName is not null
            ? $"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof({_model.BelongsToTypeName}))] {UnitOfWorkTypeName} unitOfWork"
            : $"{UnitOfWorkTypeName} unitOfWork";
        ctorParams.Add(uowParam);

        ctorParams.Add("global::System.IServiceProvider serviceProvider");

        foreach (var dep in _model.Dependencies)
        {
            var paramName = TemplateHelpers.ToCamelCase(StripLeadingUnderscore(dep.FieldName));
            ctorParams.Add($"{dep.TypeName} {paramName}");
        }

        if (_model.HasResilience)
            ctorParams.Add($"{ResilienceProviderTypeName} resiliencePipelineProvider");

        // Optional params last (C# requires optional after required)
        if (NeedsInfraCurrentUser)
            ctorParams.Add("global::Pragmatic.Identity.ICurrentUser? currentUser = null");

        if (NeedsClock)
            ctorParams.Add("global::System.TimeProvider? timeProvider = null");

        if (_model.IsRestore || _model.HasFilterOverrides)
            ctorParams.Add("global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle? filterToggle = null");

        var paramString = string.Join(",\n        ", ctorParams);

        AppendLine("public Invoker(");
        AppendLine($"        {paramString})");
        AppendLine("        : base(serviceProvider)");
        Block(() =>
        {
            if (_model.EntityIdTypeName is not null)
                AppendLine("_repository = repository;");

            AppendLine("_unitOfWork = unitOfWork;");

            if (NeedsInfraCurrentUser)
                AppendLine("_currentUser = currentUser;");

            if (NeedsClock)
                AppendLine("_timeProvider = timeProvider ?? global::System.TimeProvider.System;");

            if (_model.IsRestore || _model.HasFilterOverrides)
                AppendLine("_filterToggle = filterToggle;");

            foreach (var dep in _model.Dependencies)
            {
                var paramName = TemplateHelpers.ToCamelCase(StripLeadingUnderscore(dep.FieldName));
                AppendLine($"{TemplateHelpers.InvokerFieldName(dep.FieldName)} = {paramName};");
            }

            if (_model.HasResilience)
                AppendLine("_resiliencePipelineProvider = resiliencePipelineProvider;");
        });
    }

    private void RenderInjectDependencies()
    {
        var mutationType = _model.FullTypeName;

        AppendLine($"protected override void InjectDependencies({mutationType} mutation)");
        Block(() =>
        {
            if (_model.HasDependencies)
            {
                var args = _model.Dependencies.Select(d => TemplateHelpers.InvokerFieldName(d.FieldName));
                AppendLine($"mutation.SetDependencies({string.Join(", ", args)});");
            }
            else
            {
                Comment("No dependencies to inject");
            }
        });
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private string GetBaseClass()
        => $"global::Pragmatic.Actions.Invoker.MutationInvoker<{_model.FullTypeName}, {_model.EntityFullTypeName}>";

    private string RepositoryTypeName
        => $"global::Pragmatic.Persistence.Repository.IRepository<{_model.EntityFullTypeName}>";

    private static string UnitOfWorkTypeName => "global::Pragmatic.Persistence.Repository.IUnitOfWork";

    private static string StripLeadingUnderscore(string fieldName)
        => fieldName.StartsWith("_") ? fieldName.Substring(1) : fieldName;
}
