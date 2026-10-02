using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     Emits <c>ParentVisibilityFilter</c> on a trait's child entity, so a caller only ever sees
///     children of a parent it may itself see.
/// </summary>
/// <remarks>
///     <para>
///         The generated list query filters children by the parent id it was handed, and the child
///         carries no row restriction of its own — so anyone holding the trait's read permission could
///         enumerate the comments, notes, tags or attachments of a parent they cannot open, learning
///         file names, authors and dates. Two independent reviews of one application found it, and the
///         Showcase carried the same hole on six endpoints across <c>Guest</c> and <c>Reservation</c>.
///     </para>
///     <para>
///         The filter is the parent's own predicate lifted one level through the reference navigation
///         the trait already generates. That choice was measured, not assumed: the query-filter visitor
///         rewrites <b>collection</b> navigations only, so touching the parent from the child does not
///         inherit anything; and ownership and scope — unlike soft-delete and tenant — are not EF
///         global query filters, so a subquery over the parent's <c>DbSet</c> would not inherit them
///         either. Restating the predicate over the navigation is what the pipeline can actually apply.
///     </para>
///     <para>
///         It keeps the parent's <c>BypassPermission</c>: whoever may see every parent may list every
///         child, which is the same answer the parent gives.
///     </para>
/// </remarks>
internal sealed class TraitParentVisibilityFilterTemplate : CSharpTemplate
{
    private readonly TraitParentVisibilityModel _model;

    public TraitParentVisibilityFilterTemplate(TraitParentVisibilityModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";

    protected override string? SourceInfo =>
        $"Parent visibility filter for {_model.ChildTypeName} (parent {_model.ParentTypeName})";

    protected override bool Validate() => _model.IsOwned || _model.IsScoped;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ChildTypeName, "ParentVisibilityFilter", _model.Namespace),
            ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        var childType = $"global::{_model.Namespace}.{_model.ChildTypeName}";

        Class(_model.ChildTypeName, () =>
        {
            XmlSummary(
                $"Restricts {_model.ChildTypeName} to the rows whose {_model.ParentTypeName} the caller " +
                $"may see. Users with \"{_model.BypassPermission}\" bypass it, exactly as on the parent. " +
                "Priority 200.");

            AppendLine(
                "public sealed class ParentVisibilityFilter(" +
                Dependencies() +
                $") : global::Pragmatic.Persistence.Query.Filters.IPermissionBasedFilter<{childType}>");

            Block(() =>
            {
                ExpressionProperty("BypassPermission", "string", $"\"{_model.BypassPermission}\"");
                AppendLine();

                ExpressionProperty("Priority", "int", "200");
                AppendLine();

                ExpressionProperty("Scope", "global::Pragmatic.Persistence.Query.Filters.FilterScope",
                    "global::Pragmatic.Persistence.Query.Filters.FilterScope.Default");
                AppendLine();

                XmlSummary("Returns the parent's own visibility predicate, read through the navigation.");
                AppendLine(
                    "public global::System.Linq.Expressions.Expression<global::System.Func<" +
                    $"{childType}, bool>> GetFilter()");

                Block(RenderFilterBody);
            });
        },
        modifiers: new ClassModifiers { Partial = true });
    }

    private string Dependencies()
    {
        var parts = new List<string>();
        if (_model.IsScoped)
            parts.Add("global::Pragmatic.Authorization.IUserScopeResolver scopeResolver");
        parts.Add("global::Pragmatic.Identity.ICurrentUser currentUser");
        return string.Join(", ", parts);
    }

    private void RenderFilterBody()
    {
        if (_model.IsScoped)
            AppendLine("var userScopes = scopeResolver.ResolveAccessScopes(currentUser);");
        AppendLine("var userId = currentUser.Id;");

        // The navigation is nullable on the child, and a child whose parent row is gone must not be
        // visible either — so no null-forgiving shortcut that would let an orphan through.
        var nav = $"entity.{_model.ParentNavigationName}";
        var owned = $"{nav} != null && {nav}.OwnerId == userId";
        var scoped = $"{nav} != null && {nav}.AccessScopes.Any(s => userScopes.Contains(s))";

        var predicate = (_model.IsOwned, _model.IsScoped) switch
        {
            (true, true) => $"{owned} || {scoped}",
            (true, false) => owned,
            _ => scoped,
        };

        AppendLine($"return entity => {predicate};");
    }
}
