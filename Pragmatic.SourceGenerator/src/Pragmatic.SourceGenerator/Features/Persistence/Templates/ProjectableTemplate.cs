using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating a nested <c>Expr</c> class inside an entity,
///     containing <c>Expression&lt;Func&lt;TEntity, TResult&gt;&gt;</c> properties
///     for each [Projectable] computed property.
/// </summary>
internal sealed class ProjectableTemplate : CSharpTemplate
{
    private readonly ProjectableModel _model;

    public ProjectableTemplate(ProjectableModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Projectable] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Projectable", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq.Expressions");

        AppendNamespace(_model.Namespace);
        AppendLine();

        Class(_model.TypeName, RenderEntityBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderEntityBody()
    {
        XmlSummary("Source-generated expression projections for SQL-translatable computed properties.");

        Class("Expr", RenderExprBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderExprBody()
    {
        var entityType = $"global::{_model.FullTypeName}";

        for (var i = 0; i < _model.Properties.Length; i++)
        {
            var prop = _model.Properties[i];
            var exprType = $"Expression<Func<{entityType}, {prop.ReturnType}>>";

            XmlSummary($"Expression projection for <see cref=\"{_model.TypeName}.{prop.PropertyName}\"/>.");

            // The body as text, for a projection compiled in another assembly: it has no syntax to read
            // the body from, and the getter would run in memory over navigations nobody loaded.
            AppendLine("[global::Pragmatic.Persistence.Query.Attributes.ProjectableBody("
                       + $"{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(prop.PortableBody, true)})]");
            AppendLine($"public static {exprType} {prop.PropertyName} => e => {prop.ExpressionBody};");

            if (i < _model.Properties.Length - 1)
                AppendLine();
        }
    }

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
}
