using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     The relation's <c>OnSoftDelete</c>, put on its foreign key for <c>SoftDeleteInterceptor</c> to apply.
/// </summary>
internal sealed partial class EntityConfigurationTemplate
{
    private const string SoftDeleteRule = "global::Pragmatic.Persistence.EFCore.SoftDelete.SoftDeleteReferrerRule";

    /// <summary>
    ///     Emits the typed rule as an annotation on the relationship this navigation configures, when the relation
    ///     declares <c>OnSoftDelete</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only on the dependent's side, where the foreign key is: this entity is the referrer, the target the
    ///         principal a soft delete reaches. The query is written out typed, so the interceptor finds the
    ///         referrers without reflection.
    ///     </para>
    ///     <para>
    ///         No assignment for <c>SetNull</c>: the generated key's setter is not public, and the interceptor
    ///         empties the key through the tracker by the foreign key's properties. A required key is refused by
    ///         PRAG0640.
    ///     </para>
    /// </remarks>
    private void RenderSoftDeleteRule(NavigationMetadataModel nav)
    {
        if (nav.OnSoftDelete is not { Length: > 0 } behavior || string.IsNullOrEmpty(nav.ForeignKeyProperty))
            return;

        var principal = Global(nav.TargetFullTypeName ?? nav.TargetTypeName);
        var dependent = Global(_model.FullTypeName);

        AppendLine($".HasAnnotation({SoftDeleteRule}.Annotation, new {SoftDeleteRule}<{principal}, {dependent}>(");
        IncreaseIndent();
        AppendLine($"global::Pragmatic.Persistence.Entity.DeleteBehavior.{behavior},");
        AppendLine($"(db, principal) => global::System.Linq.Queryable.Where(db.Set<{dependent}>(), "
                   + $"d => d.{nav.ForeignKeyProperty} == principal.PersistenceId)))");
        DecreaseIndent();
    }

    private static string Global(string typeName)
        => "global::" + typeName.Replace("global::", "");
}
