using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates an IEventHandler that performs cascade updates when a source entity property changes.
///     Triggered by [CascadeOn&lt;TSource&gt;("SourceProperty")] on entity properties.
/// </summary>
internal sealed class CascadeHandlerTemplate : CSharpTemplate
{
    private readonly CascadeModel _model;
    private readonly bool _engine;

    /// <param name="model">The cascade.</param>
    /// <param name="engine">
    ///     True for a target the Sql or documents engine persists: the handler reads the rows through the target's repository, changes them and
    ///     saves the boundary's unit of work, instead of EF Core's <c>ExecuteUpdate</c>. One body for both engines, as an update by filter is not
    ///     one every store has.
    /// </param>
    public CascadeHandlerTemplate(CascadeModel model, bool engine = false)
    {
        _model = model;
        _engine = engine;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"Cascade {_model.SourceTypeName}.{_model.SourceProperty} → {_model.TargetTypeName}.{_model.TargetProperty}";
    protected override string? TriggerInfo => $"[CascadeOn<{_model.SourceTypeName}>] on {_model.TargetTypeName}.{_model.TargetProperty}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(
            $"{_model.TargetTypeName}.{_model.TargetProperty}", "CascadeHandler", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        if (!_engine)
            AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Pragmatic.Events");
        AddUsing("System.Linq");   // .Where below: declared, not left to the consumer's implicit usings (#202)

        AppendNamespace(_model.Namespace);
        AppendLine();

        var handlerName = $"{_model.TargetTypeName}{_model.TargetProperty}CascadeHandler";

        XmlSummary(
            $"Handles cascade update: when <c>{_model.SourceTypeName}.{_model.SourceProperty}</c> changes, " +
            $"updates <c>{_model.TargetTypeName}.{_model.TargetProperty}</c> for related entities.");

        Class(handlerName, RenderBody,
            baseType: $"global::Pragmatic.Events.IDomainEventHandler<global::Pragmatic.Events.EntityPropertyChanged<{_model.SourceQualifiedTypeName}>>",
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        if (_engine)
        {
            RenderEngineBody();
            return;
        }

        Field("_dbContext", "global::Microsoft.EntityFrameworkCore.DbContext", AccessModifier.Private, isReadOnly: true);
        AppendLine();

        // Constructor. In host mode the base DbContext is registered ONLY as AddKeyedScoped by boundary
        // (there is no unkeyed DbContext), so inject the target entity's boundary-keyed DbContext.
        // Console mode registers an unkeyed DbContext, used when no boundary is known.
        var handlerName = $"{_model.TargetTypeName}{_model.TargetProperty}CascadeHandler";
        var dbContextParam = string.IsNullOrEmpty(_model.TargetBoundaryTypeFullName)
            ? "global::Microsoft.EntityFrameworkCore.DbContext dbContext"
            : $"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof({Qualify(_model.TargetBoundaryTypeFullName!)}))] global::Microsoft.EntityFrameworkCore.DbContext dbContext";
        AppendLine($"public {handlerName}({dbContextParam})");
        Block(() => AppendLine("_dbContext = dbContext;"));
        AppendLine();

        // Handle method
        AppendLine(
            $"public async global::System.Threading.Tasks.Task HandleAsync(global::Pragmatic.Events.EntityPropertyChanged<{_model.SourceQualifiedTypeName}> @event, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            AppendLine($"if (@event.PropertyName != \"{_model.SourceProperty}\") return;");
            AppendLine();

            // Cast EntityId to the FK type for type-safe comparison
            AppendLine($"var entityId = ({_model.ForeignKeyTypeName})@event.EntityId;");
            AppendLine();

            if (_model.Condition is not null)
            {
                // Condition is a SQL-translatable boolean member on the target; parenthesise it so it
                // composes correctly with the FK predicate.
                AppendLine($"await _dbContext.Set<{_model.TargetFullTypeName}>()");
                AppendLine($"    .Where(e => e.{_model.ForeignKeyProperty} == entityId && (e.{_model.Condition}))");
            }
            else
            {
                AppendLine($"await _dbContext.Set<{_model.TargetFullTypeName}>()");
                AppendLine($"    .Where(e => e.{_model.ForeignKeyProperty} == entityId)");
            }

            // NewValue is boxed as object? on the event; cast it back to the target's concrete CLR type so
            // ExecuteUpdate's SetProperty produces a typed parameter the provider can map (Npgsql otherwise
            // throws "Expression '@p' ... does not have a type mapping"). See CascadeModel.TargetPropertyTypeName.
            AppendLine($"    .ExecuteUpdateAsync(s => s.SetProperty(e => e.{_model.TargetProperty}, ({_model.TargetPropertyTypeName})@event.NewValue!), ct)");
            AppendLine("    .ConfigureAwait(false);");
        });
    }

    /// <summary>
    ///     The rows the cascade reaches, read through the target's repository (the engine's filters on, as EF Core's <c>ExecuteUpdate</c> keeps
    ///     the query filters), changed on the tracked rows and saved by the boundary's unit of work, keyed as an engine registers it.
    /// </summary>
    private void RenderEngineBody()
    {
        const string unitOfWork = "global::Pragmatic.Persistence.Repository.IUnitOfWork";
        var repository = $"global::Pragmatic.Persistence.Repository.IRepository<{_model.TargetFullTypeName}>";
        var handlerName = $"{_model.TargetTypeName}{_model.TargetProperty}CascadeHandler";
        var unitOfWorkParam = string.IsNullOrEmpty(_model.TargetBoundaryTypeFullName)
            ? $"{unitOfWork} unitOfWork"
            : $"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof({Qualify(_model.TargetBoundaryTypeFullName!)}))] {unitOfWork} unitOfWork";

        Field("_targets", repository, AccessModifier.Private, isReadOnly: true);
        Field("_unitOfWork", unitOfWork, AccessModifier.Private, isReadOnly: true);
        AppendLine();
        AppendLine($"public {handlerName}({repository} targets, {unitOfWorkParam})");
        Block(() =>
        {
            AppendLine("_targets = targets;");
            AppendLine("_unitOfWork = unitOfWork;");
        });
        AppendLine();

        AppendLine(
            $"public async global::System.Threading.Tasks.Task HandleAsync(global::Pragmatic.Events.EntityPropertyChanged<{_model.SourceQualifiedTypeName}> @event, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            AppendLine($"if (@event.PropertyName != \"{_model.SourceProperty}\") return;");
            AppendLine();
            AppendLine($"var entityId = ({_model.ForeignKeyTypeName})@event.EntityId;");
            AppendLine($"var value = ({_model.TargetPropertyTypeName})@event.NewValue!;");
            var condition = _model.Condition is null ? "" : $" && (e.{_model.Condition})";
            AppendLine($"var targets = await _targets.FindAsync(global::Pragmatic.Specification.Spec<{_model.TargetFullTypeName}>");
            AppendLine($"    .Where(e => e.{_model.ForeignKeyProperty} == entityId{condition}), ct).ConfigureAwait(false);");
            AppendLine("if (targets.Count == 0) return;");
            AppendLine();
            AppendLine("foreach (var target in targets)");
            IncreaseIndent();
            AppendLine(_model.TargetSetterIsPrivate
                ? $"target.Set{_model.TargetProperty}(value);"
                : $"target.{_model.TargetProperty} = value;");
            DecreaseIndent();
            AppendLine("await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
        });
    }

    private static string Qualify(string fullTypeName) =>
        fullTypeName.StartsWith("global::", global::System.StringComparison.Ordinal)
            ? fullTypeName
            : $"global::{fullTypeName}";
}
