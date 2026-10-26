using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     A <c>VisibilityRule&lt;T&gt;</c> nested in a trait's child an engine persists, which the child declares with <c>[VisibleWhen]</c>: the
///     engines apply a declared rule wherever the child is read (root, join, subquery), from its <c>ToExpression</c> lambda. With a bypass it is
///     also an <c>IPermissionBasedFilter&lt;T&gt;</c>, which the engines lift as EF Core lifts a permission filter.
/// </summary>
/// <remarks>
///     Not the EF Core child's filters: EF Core gets the comment's moderation as a named query filter of its configuration and its internal
///     visibility as a registered <c>IPermissionBasedFilter</c>. An engine reads neither, only the child's declarations.
/// </remarks>
internal sealed class TraitEngineRuleTemplate : CSharpTemplate
{
    private readonly TraitEngineRuleModel _model;

    public TraitEngineRuleTemplate(TraitEngineRuleModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? SourceInfo => $"{_model.RuleName} of {_model.ChildTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ChildTypeName, _model.RuleName, _model.Namespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        var childType = $"global::{_model.Namespace}.{_model.ChildTypeName}";
        Class(_model.ChildTypeName, () =>
        {
            XmlSummary(_model.Summary);
            AppendLine(_model.Bypass is null
                ? $"public sealed class {_model.RuleName} : global::Pragmatic.Persistence.Query.Filters.VisibilityRule<{childType}>"
                : $"public sealed class {_model.RuleName} : global::Pragmatic.Persistence.Query.Filters.VisibilityRule<{childType}>, "
                  + $"global::Pragmatic.Persistence.Query.Filters.IPermissionBasedFilter<{childType}>");
            Block(() =>
            {
                if (_model.Bypass is { } bypass)
                {
                    ExpressionProperty("BypassPermission", "string", $"\"{bypass}\"");
                    AppendLine();
                }

                AppendLine($"public override global::System.Linq.Expressions.Expression<global::System.Func<{childType}, bool>> ToExpression() => e => {_model.Predicate};");
            });
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { Partial = true });
    }
}
