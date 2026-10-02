using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Lifecycle rendering methods: delete, restore, cascade compensation, computed defaults, and presets.
/// </summary>
internal sealed partial class MutationInvokerTemplate
{
    private void RenderDeleteEntity()
    {
        var entityType = _model.EntityFullTypeName;

        AppendLine($"protected override void DeleteEntity({entityType} entity)");
        Block(() =>
        {
            if (!_model.IsDelete)
            {
                Comment("Not a delete mutation — no-op");
                return;
            }

            if (_model.HasSoftDelete)
            {
                var cascades = _model.SoftDelete?.HasCascadeTargets == true;

                Comment("Soft delete: mark entity as deleted (ISoftDelete properties are public)");

                if (cascades)
                {
                    // One instant for the whole cascade, and it is what makes the restore exact: a child
                    // hidden by this delete carries the parent's DeletedAt, so RestoreEntity can bring
                    // back those and only those. A per-child UtcNow differs by ticks and would make the
                    // two indistinguishable from a child deleted on its own a moment earlier.
                    AppendLine("var deletedAt = _timeProvider.GetUtcNow();");
                    AppendLine("var deletedBy = _currentUser?.IdOrNull();");
                    AppendLine();
                    AppendLine("entity.IsDeleted = true;");
                    AppendLine("entity.DeletedAt = deletedAt;");
                    AppendLine("entity.DeletedBy = deletedBy;");
                }
                else
                {
                    AppendLine("entity.IsDeleted = true;");
                    AppendLine("entity.DeletedAt = _timeProvider.GetUtcNow();");
                    AppendLine("entity.DeletedBy = _currentUser?.IdOrNull();");
                }

                if (cascades)
                {
                    AppendLine();
                    Comment("Cascade soft-delete to related entities");
                    foreach (var target in _model.SoftDelete!.CascadeTargets)
                    {
                        var local = TemplateHelpers.ToCamelCase(target.PropertyName);

                        if (target.IsCollection)
                        {
                            AppendLine($"if (entity.{target.PropertyName} is {{ }} {local})");
                            Block(() =>
                            {
                                AppendLine($"foreach (var item in {local})");
                                Block(() =>
                                {
                                    Comment("Already deleted on its own: left alone, so its own DeletedAt");
                                    Comment("survives and the restore can tell the two apart.");
                                    AppendLine("if (item.IsDeleted)");
                                    AppendLine("    continue;");
                                    AppendLine("item.IsDeleted = true;");
                                    AppendLine("item.DeletedAt = deletedAt;");
                                    AppendLine("item.DeletedBy = deletedBy;");
                                });
                            });
                        }
                        else
                        {
                            AppendLine($"if (entity.{target.PropertyName} is {{ IsDeleted: false }} {local})");
                            Block(() =>
                            {
                                AppendLine($"{local}.IsDeleted = true;");
                                AppendLine($"{local}.DeletedAt = deletedAt;");
                                AppendLine($"{local}.DeletedBy = deletedBy;");
                            });
                        }
                    }
                }
            }
            else
            {
                if (_model.EntityIdTypeName is not null)
                {
                    AppendLine("_repository.Remove(entity);");
                }
                else
                {
                    AppendLine(
                        "throw new global::System.NotSupportedException(\"Entity must implement IEntity for hard delete.\");");
                }
            }
        });
    }

    private void RenderRestoreEntity()
    {
        var entityType = _model.EntityFullTypeName;

        var cascades = _model.SoftDelete?.HasCascadeTargets == true;

        XmlSummary("Restores a soft-deleted entity by resetting ISoftDelete fields.");
        AppendLine($"protected override void RestoreEntity({entityType} entity)");
        Block(() =>
        {
            if (cascades)
            {
                // Read before clearing: this is the stamp the delete put on everything it hid, and the
                // only thing that separates "hidden because the parent went" from "hidden on its own".
                // Restoring every hidden child instead would resurrect the ones somebody had deleted
                // deliberately, which is the reason a restore cascade is usually refused outright.
                AppendLine("var hiddenAt = entity.DeletedAt;");
                AppendLine();
            }

            Comment("Reset soft-delete fields");
            AppendLine("entity.IsDeleted = false;");
            AppendLine("entity.DeletedAt = null;");
            AppendLine("entity.DeletedBy = null;");

            if (!cascades)
                return;

            AppendLine();
            Comment("Undo exactly what this entity's delete hid, and nothing else");
            foreach (var target in _model.SoftDelete!.CascadeTargets)
            {
                var local = TemplateHelpers.ToCamelCase(target.PropertyName);

                if (target.IsCollection)
                {
                    AppendLine($"if (entity.{target.PropertyName} is {{ }} {local})");
                    Block(() =>
                    {
                        AppendLine($"foreach (var item in {local})");
                        Block(() =>
                        {
                            AppendLine("if (!item.IsDeleted || item.DeletedAt != hiddenAt)");
                            AppendLine("    continue;");
                            AppendLine("item.IsDeleted = false;");
                            AppendLine("item.DeletedAt = null;");
                            AppendLine("item.DeletedBy = null;");
                        });
                    });
                }
                else
                {
                    AppendLine($"if (entity.{target.PropertyName} is {{ IsDeleted: true }} {local} && {local}.DeletedAt == hiddenAt)");
                    Block(() =>
                    {
                        AppendLine($"{local}.IsDeleted = false;");
                        AppendLine($"{local}.DeletedAt = null;");
                        AppendLine($"{local}.DeletedBy = null;");
                    });
                }
            }
        });
    }

    private void RenderCompensateSoftDeleteCascade()
    {
        var entityType = _model.EntityFullTypeName;

        XmlSummary("Compensates cascade targets after a failed SaveChangesAsync by reverting soft-delete fields.");
        AppendLine($"protected override void CompensateSoftDeleteCascade({entityType} entity)");
        Block(() =>
        {
            if (_model.SoftDelete is null) return;

            foreach (var target in _model.SoftDelete.CascadeTargets)
            {
                if (target.IsCollection)
                {
                    AppendLine($"if (entity.{target.PropertyName} is {{ }} {TemplateHelpers.ToCamelCase(target.PropertyName)})");
                    Block(() =>
                    {
                        AppendLine($"foreach (var item in {TemplateHelpers.ToCamelCase(target.PropertyName)})");
                        Block(() =>
                        {
                            Comment("Revert to non-deleted state (best-effort compensation)");
                            AppendLine("item.IsDeleted = false;");
                            AppendLine("item.DeletedAt = null;");
                            AppendLine("item.DeletedBy = null;");
                        });
                    });
                }
                else
                {
                    AppendLine($"if (entity.{target.PropertyName} is {{ }} {TemplateHelpers.ToCamelCase(target.PropertyName)})");
                    Block(() =>
                    {
                        AppendLine($"{TemplateHelpers.ToCamelCase(target.PropertyName)}.IsDeleted = false;");
                        AppendLine($"{TemplateHelpers.ToCamelCase(target.PropertyName)}.DeletedAt = null;");
                        AppendLine($"{TemplateHelpers.ToCamelCase(target.PropertyName)}.DeletedBy = null;");
                    });
                }
            }
        });
    }

    private void RenderApplyComputedDefaultsAsync()
    {
        var entityType = _model.EntityFullTypeName;
        var contextType = "global::Pragmatic.Persistence.Lifecycle.LifecycleContext";

        XmlSummary("Applies computed default values to a newly created entity via registered generators.");

        AppendLine($"protected override async global::System.Threading.Tasks.Task ApplyComputedDefaultsAsync({entityType} entity, {contextType} context, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            if (_model.ComputedDefaults is null) return;

            foreach (var prop in _model.ComputedDefaults.Properties)
            {
                var generatorInterface = $"global::Pragmatic.Persistence.Lifecycle.IDefaultValueGenerator<{prop.EntityTypeFqn}, {prop.ValueTypeFqn}>";

                var varName = TemplateHelpers.ToCamelCase(prop.PropertyName);
                AppendLine($"var {varName}Generator = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{generatorInterface}>(_serviceProvider);");
                AppendLine($"var {varName}Value = await {varName}Generator.GenerateAsync(entity, context, ct).ConfigureAwait(false);");

                // Public setter: direct assignment. Private setter: use generated SetXxx method.
                if (prop.SetterName == prop.PropertyName)
                    AppendLine($"entity.{prop.PropertyName} = {varName}Value;");
                else
                    AppendLine($"entity.{prop.SetterName}({varName}Value);");
                AppendLine();
            }
        });
    }

    private void RenderApplyPresetsAsync()
    {
        var entityType = _model.EntityFullTypeName;
        var contextType = "global::Pragmatic.Persistence.Lifecycle.LifecycleContext";

        XmlSummary("Creates preset child entities via registered preset providers.");

        AppendLine($"protected override async global::System.Threading.Tasks.Task ApplyPresetsAsync({entityType} entity, {contextType} context, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            if (_model.Presets is null) return;

            foreach (var provider in _model.Presets.Providers)
            {
                var providerInterface = $"global::Pragmatic.Persistence.Lifecycle.IPresetProvider<{entityType}>";
                var varName = TemplateHelpers.ToCamelCase(provider.ProviderTypeFqn.Split('.').Last());

                AppendLine($"var {varName} = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{provider.ProviderTypeFqn}>(_serviceProvider);");
                AppendLine($"var {varName}Presets = await {varName}.CreatePresetsAsync(entity, context, ct).ConfigureAwait(false);");
                AppendLine($"foreach (var preset in {varName}Presets)");
                Block(() => AppendLine("_unitOfWork.Add(preset);"));
                AppendLine();
            }
        });
    }
}
