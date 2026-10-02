using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates Specification&lt;T&gt; fields and WhereXxx() extension methods
///     for [ComputedFilter] boolean properties.
/// </summary>
internal sealed class ComputedFilterTemplate : CSharpTemplate
{
    private readonly ComputedFilterModel _model;

    public ComputedFilterTemplate(ComputedFilterModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[ComputedFilter] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "ComputedFilter", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Specification");

        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        var entityType = $"global::{_model.FullTypeName}";
        var className = NamingHelper.AppendSuffix(_model.TypeName, "ComputedFilters");

        XmlSummary($"Source-generated computed filter specifications and query extensions for {_model.TypeName}.");

        Class(className, () => RenderBody(entityType),
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody(string entityType)
    {
        for (var i = 0; i < _model.Properties.Length; i++)
        {
            var prop = _model.Properties[i];

            if (prop.IsMethod)
            {
                RenderMethodFilter(entityType, prop);
                if (i < _model.Properties.Length - 1)
                    AppendLine();
                continue;
            }

            // Specification field
            XmlSummary($"Reusable specification for {_model.TypeName}.{prop.PropertyName}.");
            AppendLine($"public static readonly global::Pragmatic.Specification.Specification<{entityType}> {prop.PropertyName}Spec =");
            IncreaseIndent();
            AppendLine($"global::Pragmatic.Specification.Spec<{entityType}>.Where(e => {prop.ExpressionBody});");
            DecreaseIndent();

            AppendLine();

            // WhereXxx extension method
            var methodName = GetWhereMethodName(prop.PropertyName);
            XmlSummary($"Filters the query to include only {_model.TypeName} entities where {prop.PropertyName} is true.");
            XmlParam("query", "The source query.");
            XmlReturns("Filtered query.");
            AppendLine($"public static global::System.Linq.IQueryable<{entityType}> {methodName}(this global::System.Linq.IQueryable<{entityType}> query)");
            IncreaseIndent();
            AppendLine($"=> query.Where({prop.PropertyName}Spec.ToExpression());");
            DecreaseIndent();

            if (i < _model.Properties.Length - 1)
                AppendLine();
        }
    }

    /// <summary>
    ///     A method filter: a specification built for the values it is given, and an extension that
    ///     passes them on. A method, not a field, because each value is a different condition.
    /// </summary>
    private void RenderMethodFilter(string entityType, ComputedFilterPropertyModel prop)
    {
        var specType = $"global::Pragmatic.Specification.Specification<{entityType}>";

        XmlSummary($"Reusable specification for {_model.TypeName}.{prop.PropertyName}, for the values given.");
        AppendLine($"public static {specType} {prop.PropertyName}Spec({prop.Parameters})");
        IncreaseIndent();
        AppendLine($"=> global::Pragmatic.Specification.Spec<{entityType}>.Where(e => {prop.ExpressionBody});");
        DecreaseIndent();

        AppendLine();

        var methodName = GetWhereMethodName(prop.PropertyName);
        var parameters = string.IsNullOrEmpty(prop.Parameters) ? "" : $", {prop.Parameters}";
        XmlSummary($"Filters the query to include only {_model.TypeName} entities where {prop.PropertyName} is true for the values given.");
        XmlParam("query", "The source query.");
        // Every parameter documented once one is: a project that builds its documentation otherwise
        // stops on CS1573, a warning the examples treat as an error.
        foreach (var argument in (prop.Arguments ?? "").Split([", "], StringSplitOptions.RemoveEmptyEntries))
            XmlParam(argument.TrimStart('@'), $"Passed to {_model.TypeName}.{prop.PropertyName}.");
        XmlReturns("Filtered query.");
        AppendLine($"public static global::System.Linq.IQueryable<{entityType}> {methodName}(this global::System.Linq.IQueryable<{entityType}> query{parameters})");
        IncreaseIndent();
        AppendLine($"=> query.Where({prop.PropertyName}Spec({prop.Arguments}).ToExpression());");
        DecreaseIndent();
    }

    /// <summary>
    ///     Converts "IsOverdue" → "WhereOverdue", "HasBalance" → "WhereHasBalance".
    /// </summary>
    private static string GetWhereMethodName(string propertyName)
    {
        // Strip "Is" prefix if present for cleaner method names
        if (propertyName.StartsWith("Is", StringComparison.Ordinal) && propertyName.Length > 2 &&
            char.IsUpper(propertyName[2]))
        {
            return $"Where{propertyName.Substring(2)}";
        }

        return $"Where{propertyName}";
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            _ => AccessModifier.Public
        };
    }
}
