using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A grouped chain: <c>GroupBy(o => o.Field)</c> then <c>Select(g => new { g.Key, N = g.Count(), T = g.Sum(o => o.Total) })</c> into a
///     <c>$group</c> stage (<c>_id</c> the key, one accumulator per member: <c>$sum: 1</c>, <c>$sum/$min/$max/$avg</c> of a field) and a reader
///     building the same anonymous object from each result document (in the same assembly the shape is the same type). After the Select, a
///     Where over its members is a <c>$match</c> on the groups (HAVING) and an ordering a <c>$sort</c>, both against the result's model.
/// </summary>
internal sealed partial class DocPlanner
{
    private string? _groupKey;

    /// <summary>The <c>$group</c> stage's elements as statements over <c>w</c>; null for an ungrouped chain.</summary>
    private string? _group;

    /// <summary>The reader of a result document (<c>d</c>) into the Select's object, boxed.</summary>
    private string? _groupRead;

    /// <summary>The grouped Select's result as a model (Key as <c>_id</c>, each aggregate under its name), for the steps after it.</summary>
    private DocModel? _groupModel;

    /// <summary>The conditions of the Wheres after the grouped Select (HAVING).</summary>
    private readonly List<DocCondition> _having = [];

    /// <summary>The ordering of the groups, by the result's members.</summary>
    private readonly List<(string Path, bool Descending)> _groupSort = [];

    /// <summary>True when the chain ends in a grouped Select: the terminal reads groups.</summary>
    public bool Grouped => _group is not null;

    private void GroupBy(DocStep step)
    {
        if (_groupKey is not null || _sort.Count > 0)
        {
            Fail(step.Invocation, "one GroupBy, without an ordering before it (the groups have no order)");
            return;
        }

        _groupKey = KeyPath(step.Lambda, step.Invocation);
    }

    private void GroupSelect(DocStep step)
    {
        var parameters = Parameters(step.Lambda);
        if (step.Lambda?.Body is not AnonymousObjectCreationExpressionSyntax creation || parameters.Count != 1)
        {
            Fail(step.Invocation, "a grouped Select builds a new object of the key and the aggregates: g => new { g.Key, Count = g.Count(), Total = g.Sum(o => o.Total) }");
            return;
        }

        var group = parameters[0];
        var stage = new StringBuilder($"w.Write(\"_id\", {DocTypeEmitter.Literal("$" + _groupKey)}); ");
        var members = new List<string>();
        var fieldOf = new Dictionary<string, string>();
        foreach (var initializer in creation.Initializers)
        {
            var expression = initializer.Expression;
            var name = initializer.NameEquals?.Name.Identifier.ValueText ?? (expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText;
            var type = _call.Model.GetTypeInfo(expression).Type;
            if (name is null || type is null)
            {
                Fail(expression, "each member of a grouped Select is named: Count = g.Count()");
                return;
            }

            string field;
            if (expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Key" } key && IsGroup(key.Expression, group))
            {
                field = "_id";
            }
            else if (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call && IsGroup(access.Expression, group) && access.Name.Identifier.ValueText is "Count" or "Sum" or "Min" or "Max" or "Average")
            {
                var function = access.Name.Identifier.ValueText;
                var arguments = call.ArgumentList.Arguments;
                if (function == "Count" && arguments.Count == 0)
                {
                    stage.Append($"w.StartDocument({DocTypeEmitter.Literal(name)}); w.Write(\"$sum\", 1); w.EndDocument(); ");
                }
                else if (function != "Count" && arguments.Count == 1 && arguments[0].Expression is SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } lambda && _call.Model.GetDeclaredSymbol(lambda.Parameter) is IParameterSymbol row)
                {
                    var translator = new DocTranslator(_call.Model, _entity, _models, row, null, null, _diagnostics, call.GetLocation());
                    if (translator.Path(body, out var source) is not { } path || source is null || source.Kind is DocKind.Embedded or DocKind.List)
                    {
                        Fail(call, $"{function} over a group takes a field of the document: g.{function}(o => o.Total)");
                        return;
                    }

                    var accumulator = function == "Average" ? "$avg" : "$" + function.ToLowerInvariant();
                    stage.Append($"w.StartDocument({DocTypeEmitter.Literal(name)}); w.Write(\"{accumulator}\", {DocTypeEmitter.Literal("$" + path)}); w.EndDocument(); ");
                }
                else
                {
                    Fail(call, "an aggregate of a group is g.Count(), or g.Sum/Min/Max/Average(o => o.Field)");
                    return;
                }

                field = name;
            }
            else
            {
                Fail(expression, "a member of a grouped Select is g.Key or an aggregate of the group (Count, Sum, Min, Max, Average)");
                return;
            }

            members.Add($"{name} = {Read(type, field)}");
            fieldOf[name] = field;
        }

        if (_call.Model.GetTypeInfo(creation).Type is INamedTypeSymbol result)
            _groupModel = DocModel.Group(result, fieldOf);
        _group = stage.ToString();
        _groupRead = $"static d => (object)new {{ {string.Join(", ", members)} }}";
    }

    private bool IsGroup(ExpressionSyntax expression, IParameterSymbol group)
        => expression is IdentifierNameSyntax identifier && SymbolEqualityComparer.Default.Equals(_call.Model.GetSymbolInfo(identifier).Symbol, group);

    /// <summary>A result field read as the member's type (an enum through its Int32).</summary>
    private static string Read(ITypeSymbol type, string field)
    {
        const string values = "global::Pragmatic.Persistence.Documents.DocValues";
        var nullable = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapper ? wrapper.TypeArguments[0] : null;
        var underlying = nullable ?? type;
        if (underlying.TypeKind == TypeKind.Enum)
            return $"({type.ToDisplayString(DocModel.Qualified)}){values}.ReadScalar<int>(d, {DocTypeEmitter.Literal(field)})";
        return $"{values}.ReadScalar<{type.ToDisplayString(DocModel.Qualified)}>(d, {DocTypeEmitter.Literal(field)})";
    }

    /// <summary>The plan's group arguments: the <c>$group</c> stage, the reader, the HAVING filter and the groups' <c>$sort</c>.</summary>
    private string GroupInitializer
    {
        get
        {
            if (_group is null)
                return "null, null, null, null";
            var source = new StringBuilder($"static w => {{ {_group}}}, {_groupRead}, ");
            if (DocCondition.All(_having) is { } having)
            {
                source.AppendLine("static (w, s) =>");
                source.AppendLine("            {");
                having.Render(source, "                ");
                source.Append("            }, ");
            }
            else
            {
                source.Append("null, ");
            }

            source.Append(_groupSort.Count == 0 ? "null" : "static w => { " + string.Concat(_groupSort.Select(k => $"w.Write({DocTypeEmitter.Literal(k.Path)}, {(k.Descending ? -1 : 1)}); ")) + "}");
            return source.ToString();
        }
    }
}
