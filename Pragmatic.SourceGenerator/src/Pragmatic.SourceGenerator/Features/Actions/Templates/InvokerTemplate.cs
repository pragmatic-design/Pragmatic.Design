using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

internal sealed partial class InvokerTemplate : CSharpTemplate
{
    private readonly ActionModel _model;

    public InvokerTemplate(ActionModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[DomainAction] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Invoker", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_model.Namespace);
        AppendLine();

        var baseClass = GetInvokerBaseClass();

        // Nested inside partial action class (1 nested class per file)
        Class(_model.TypeName, () =>
        {
            XmlSummary($"Generated invoker for <see cref=\"{_model.TypeName}\"/>. Resolves dependencies from DI and injects them before execution.");

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
    ///     The case that exists today is the boundary's <c>internal</c> facade. This is possible only
    ///     because the host stopped writing out every module's invoker registrations and calls the
    ///     module's own instead: while it named these types from another assembly, they had to be
    ///     public, and the dependency had to come out of the provider rather than the constructor.
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
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }

    /// <summary>
    ///     Whether this operation belongs to a <b>boundary</b> — the thing that has a unit of work.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A package is not one, which is why this is not just a null check. A package's
    ///     actions declare their package with <c>[BelongsTo&lt;TPackage&gt;]</c>; read as a boundary,
    ///     the invoker would take an <c>IUnitOfWork</c> keyed by the package type, nothing registers that,
    ///     and importing the package would stop the host at container validation — with the error naming
    ///     a generated invoker and neither the attribute nor the fix. A package has no unit of work:
    ///     its actions persist through the store they hold.
    /// </remarks>
    private bool HasBoundary => !string.IsNullOrEmpty(_model.BelongsToTypeName) && !_model.BelongsToIsPackage;

    /// <summary>
    ///     Whether this dependency is the unit of work the invoker already holds under that very name.
    /// </summary>
    /// <remarks>
    ///     An operation may declare <c>private IUnitOfWork _unitOfWork</c> — the natural name — and the
    ///     invoker of an operation that belongs to a boundary declares a field of the same name and
    ///     type for its own commit. Emitting both is a duplicate field and a duplicate parameter, so
    ///     the dependency reuses the one already there. A field named differently (<c>_uow</c>) gets
    ///     its own parameter, resolved with the same key: redundant, and correct.
    /// </remarks>
    private bool IsTheInvokersOwnUnitOfWork(Models.DependencyModel dep)
        => HasBoundary
           && TemplateHelpers.InvokerFieldName(dep.FieldName) == "_unitOfWork"
           && dep.TypeName == "global::Pragmatic.Persistence.Repository.IUnitOfWork";

    private void RenderInvokerBody()
    {
        if (HasBoundary)
        {
            Field("_unitOfWork", "global::Pragmatic.Persistence.Repository.IUnitOfWork", AccessModifier.Private, isReadOnly: true);
            AppendLine();
        }

        if (_model.HasResilience)
        {
            Field("_resiliencePipelineProvider", "global::Pragmatic.Resilience.IResiliencePipelineProvider", AccessModifier.Private, isReadOnly: true);
            AppendLine();
        }

        if (_model.HasFilterOverrides)
        {
            Field("_filterToggle", "global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle?", AccessModifier.Private, isReadOnly: true);
            AppendLine();
        }

        if (_model.HasDelegationScope)
        {
            Field("_delegationService", "global::Pragmatic.Authorization.Delegation.IDelegationService?", AccessModifier.Private, isReadOnly: true);
            AppendLine();
        }

        foreach (var dep in _model.Dependencies)
        {
            if (IsTheInvokersOwnUnitOfWork(dep))
                continue;
            Field(TemplateHelpers.InvokerFieldName(dep.FieldName), dep.TypeName, AccessModifier.Private, isReadOnly: true);
        }

        if (_model.HasCompositeSteps)
            Field("_compositeInvoker", "CompositeInvoker", AccessModifier.Private, isReadOnly: true);

        if (_model.HasDependencies || HasBoundary || _model.HasResilience || _model.HasFilterOverrides || _model.HasCompositeSteps)
            AppendLine();

        RenderConstructor();
        AppendLine();
        RenderInjectDependencies();

        if (HasBoundary)
        {
            AppendLine();
            RenderSaveChangesAsync();
            AppendLine();
            RenderCommitOwnership();
        }

        // A composite with steps commits inside its CompositeInvoker; the pipeline here must not commit
        // a second time. SaveChangesAsync stays available — the declared undo commits through it.
        if (_model.HasCompositeSteps)
        {
            AppendLine();
            AppendLine("protected override bool CommitsItsOwnWork => true;");
        }

        if (new PreparationHook(_model.Bindings, _model.HasLoadEntities, _model.LoadedValidation, _model.LoadFromQueries).IsNeeded)
        {
            AppendLine();
            RenderPrepareActionAsync();
        }

        // The rules of what it loaded, and only the ones it can answer. Omitted entirely
        // when there are none, so an operation that loads nothing carries no empty override.
        if (_model.LoadEntities.Any(load => load.CheckableInvariants.Count > 0))
        {
            AppendLine();
            RenderCheckLoadedInvariants();
        }

        // ⚠️ HasDelegationScope is NOT here: the delegation has to be
        // open when the invoker saves, which happens after this override returns. It goes on
        // BeginInvocationScope instead, which the pipeline holds across the save. AbsorbsChildPermissions
        // stays: it covers what the body invokes, and the body is exactly this override.
        if (_model.IsComposite || _model.HasVersioning || _model.HasResilience || _model.HasFilterOverrides
            || _model.AbsorbsChildPermissions)
        {
            AppendLine();
            RenderExecuteActionAsync();
        }

        // [Cacheable]: the pipeline reads the cache only when told the action is cacheable.
        // Not for a void action, which has no answer to keep.
        if (_model is { IsCacheable: true, IsVoid: false })
        {
            AppendLine();
            AppendLine(
                $"protected override global::Pragmatic.Caching.ICacheable? CacheableRead({_model.FullTypeName} action) => action;");
        }

        // The scope the pipeline holds for the whole invocation, saves and post-commit effects
        // included. Leaving it out would make [StartsDelegation] compile and do nothing.
        if (_model.HasDelegationScope)
        {
            AppendLine();
            RenderBeginInvocationScope();
        }

        if (_model.HasRaisedEvents)
        {
            AppendLine();
            RenderCollectRaisedEvents();
        }

        RenderTransition();

        if (_model.CompensatorTypeName is not null)
        {
            AppendLine();
            RenderCompensation();
        }
    }

    /// <summary>
    ///     The undo declared by <c>[UndoWith&lt;T&gt;]</c>. The commit is this invoker's own, so
    ///     the undo lands in the boundary that owns the data — resolving the callee's unit of work from
    ///     the compensator instead would put the write in whichever boundary happened to be keyed last.
    /// </summary>
    private void RenderCompensation()
    {
        AppendLine("protected override bool IsCompensable => true;");
        AppendLine();

        XmlSummary("Runs the declared compensator, then commits it through this invoker's unit of work.");

        var parameters = _model.IsVoid
            ? "global::System.Threading.CancellationToken ct"
            : $"{_model.ReturnTypeName ?? "object"} committed, global::System.Threading.CancellationToken ct";
        var undoArguments = _model.IsVoid ? "ct" : "committed, ct";

        AppendLine(
            "protected override async global::System.Threading.Tasks.Task<global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>> " +
            $"CompensateAsync({parameters})");
        Block(() =>
        {
            AppendLine(
                $"var compensator = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{_model.CompensatorTypeName}>(ServiceProvider);");
            AppendLine($"var undone = await compensator.Undo({undoArguments}).ConfigureAwait(false);");
            AppendLine("if (undone.IsFailure)");
            AppendLine("    return undone;");
            AppendLine();

            if (HasBoundary)
            {
                Comment("The undo staged its writes; without this commit it would be a no-op that reports success.");
                AppendLine("await SaveChangesAsync(ct).ConfigureAwait(false);");
            }
            else
            {
                Comment("No boundary, so no unit of work: the compensator owns its own persistence.");
            }

            AppendLine("return global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>.Success();");
        });
    }

    private void RenderCollectRaisedEvents()
    {
        XmlSummary("Builds the domain events declared via [Raises&lt;T&gt;], filled by name from the action's inputs.");

        var resultParam = _model.IsVoid ? "" : $", {_model.ReturnTypeName ?? "object"} result";
        var parts = _model.RaisedEvents
            .Select(ev => $"new {ev.EventFullName}({string.Join(", ", ev.ConstructorArguments)})");

        AppendLine(
            "protected override global::System.Collections.Generic.IReadOnlyList<global::Pragmatic.Events.IDomainEvent> " +
            $"CollectRaisedEvents({_model.TypeName} action{resultParam})");
        AppendLine($"    => [{string.Join(", ", parts)}];");
    }

    private void RenderConstructor()
    {
        var ctorParams = new List<string>();

        if (HasBoundary)
            ctorParams.Add($"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof({_model.BelongsToTypeName}))] global::Pragmatic.Persistence.Repository.IUnitOfWork unitOfWork");

        if (_model.HasResilience)
            ctorParams.Add("global::Pragmatic.Resilience.IResiliencePipelineProvider resiliencePipelineProvider");

        foreach (var d in _model.Dependencies)
        {
            if (IsTheInvokersOwnUnitOfWork(d))
                continue;

            var paramName = TemplateHelpers.ToCamelCase(StripUnderscore(d.FieldName));
            if (!string.IsNullOrEmpty(d.KeyedServiceType))
                ctorParams.Add($"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof({d.KeyedServiceType}))] {d.TypeName} {paramName}");
            else
                ctorParams.Add($"{d.TypeName} {paramName}");
        }

        if (_model.HasCompositeSteps)
            ctorParams.Add("CompositeInvoker compositeInvoker");

        ctorParams.Add("global::System.IServiceProvider serviceProvider");

        // Optional params last (C# requires optional after required)
        if (_model.HasFilterOverrides)
            ctorParams.Add("global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle? filterToggle = null");

        if (_model.HasDelegationScope)
            ctorParams.Add("global::Pragmatic.Authorization.Delegation.IDelegationService? delegationService = null");

        var paramString = string.Join(",\n        ", ctorParams);

        AppendLine("public Invoker(");
        AppendLine($"        {paramString})");
        AppendLine("        : base(serviceProvider)");
        Block(() =>
        {
            if (HasBoundary)
                AppendLine("_unitOfWork = unitOfWork;");
            if (_model.HasResilience)
                AppendLine("_resiliencePipelineProvider = resiliencePipelineProvider;");
            if (_model.HasFilterOverrides)
                AppendLine("_filterToggle = filterToggle;");
            if (_model.HasDelegationScope)
                AppendLine("_delegationService = delegationService;");
            if (_model.HasCompositeSteps)
                AppendLine("_compositeInvoker = compositeInvoker;");

            foreach (var dep in _model.Dependencies)
            {
                if (IsTheInvokersOwnUnitOfWork(dep))
                    continue;

                var field = TemplateHelpers.InvokerFieldName(dep.FieldName);

                var paramName = TemplateHelpers.ToCamelCase(StripUnderscore(dep.FieldName));
                AppendLine($"{field} = {paramName};");
            }
        });
    }

    private void RenderInjectDependencies()
    {
        XmlSummary($"Injects dependencies into the <see cref=\"{_model.TypeName}\"/> instance.");

        if (_model.HasDependencies)
        {
            AppendLine($"protected override void InjectDependencies({_model.TypeName} action)");
            Block(() =>
            {
                var args = _model.Dependencies.Select(d => TemplateHelpers.InvokerFieldName(d.FieldName));
                AppendLine($"action.SetDependencies({string.Join(", ", args)});");
            });
        }
        else
        {
            AppendLine($"protected override void InjectDependencies({_model.TypeName} action)");
            Block(() => Comment("No dependencies to inject"));
        }
    }

    /// <summary>
    ///     What <c>CommitScope</c> keys ownership on, and the strategy that governs it.
    /// </summary>
    /// <remarks>
    ///     The unit of work is exposed by identity, not by name: two boundaries resolve two different
    ///     instances, and that difference is exactly what stops an outer invoker from claiming a commit
    ///     it cannot perform.
    /// </remarks>
    private void RenderCommitOwnership()
    {
        AppendLine(
            "protected override global::Pragmatic.Persistence.Repository.IUnitOfWork? UnitOfWork => _unitOfWork;");

        // Not for a composite: that one delegates the whole unit of work to its CompositeInvoker —
        // CommitsItsOwnWork above says so — and the transaction is part of it. Declaring it here as
        // well had both invokers call BeginTransactionAsync on the same connection, which EF Core
        // refuses at run time with "the connection is already in a transaction". Found by a consumer
        // application: no composite in this repository is transactional.
        if (_model is { IsTransactional: true, HasCompositeSteps: false })
        {
            AppendLine();
            AppendLine("protected override bool IsTransactional => true;");
        }

        if (_model.CommitMode is null)
            return;

        AppendLine();
        AppendLine(
            $"protected override global::Pragmatic.Actions.Commit.CommitMode CommitMode => global::Pragmatic.Actions.Commit.CommitMode.{_model.CommitMode};");
    }

    private void RenderSaveChangesAsync()
    {
        XmlSummary("Persists pending changes via the boundary-keyed IUnitOfWork.");
        AppendLine("protected override global::System.Threading.Tasks.Task SaveChangesAsync(global::System.Threading.CancellationToken ct)");
        AppendLine("    => _unitOfWork.SaveChangesAsync(ct);");
    }

    private string GetInvokerBaseClass()
    {
        if (_model.IsVoid)
            return $"global::Pragmatic.Actions.Invoker.VoidDomainActionInvoker<{_model.FullTypeName}>";

        var returnType = _model.ReturnTypeName ?? "object";
        return $"global::Pragmatic.Actions.Invoker.DomainActionInvoker<{_model.FullTypeName}, {returnType}>";
    }

    private static string StripUnderscore(string fieldName)
        => fieldName.StartsWith("_") ? fieldName.Substring(1) : fieldName;
}
