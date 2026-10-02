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

    public CascadeHandlerTemplate(CascadeModel model) => _model = model;

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
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Pragmatic.Events");

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

    private static string Qualify(string fullTypeName) =>
        fullTypeName.StartsWith("global::", global::System.StringComparison.Ordinal)
            ? fullTypeName
            : $"global::{fullTypeName}";
}
