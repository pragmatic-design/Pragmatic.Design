using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     An aggregate of a group, as LINQ spells it on the grouping parameter: <c>g.Count()</c>, <c>g.Count(x => pred)</c>,
///     <c>g.Where(x => pred).Sum(x => value)</c> (a filtered aggregate: <c>SUM(CASE WHEN pred THEN value END)</c>, the one form every
///     dialect has), <c>g.Select(x => col).Distinct().Count()</c> (<c>COUNT(DISTINCT col)</c>), <c>string.Join(sep, g.Select(x => col))</c>
///     (<c>STRING_AGG</c>, <c>group_concat</c> on SQLite). Read by the grouped projection and by the translator (HAVING, ORDER BY).
/// </summary>
internal static class GroupAggregate
{
    public enum Kind
    {
        Count,
        Average,
        Value,
        Text,
    }

    /// <summary>
    ///     The SQL of the aggregate, or null with <paramref name="error" />. <paramref name="translate" /> gives a translator over the
    ///     group's row for a lambda; <paramref name="piece" /> registers a dialect piece and returns its token.
    /// </summary>
    public static string? Render(ExpressionSyntax expression, SemanticModel model, IParameterSymbol group, Func<LambdaExpressionSyntax, LambdaTranslator?> translate, Func<DialectPiece, string> piece, out Kind kind, out string? error)
    {
        kind = Kind.Value;
        error = null;
        string? separator = null;
        var invocation = expression as InvocationExpressionSyntax;
        // string.Join(sep, …): the separator a constant, the sequence the chain on the group.
        if (invocation is { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Join" } join } && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType.SpecialType: SpecialType.System_String } && invocation.ArgumentList.Arguments.Count == 2)
        {
            var constant = model.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression);
            if (!constant.HasValue || constant.Value is not string text)
            {
                error = "string.Join over a group takes a constant separator";
                return null;
            }

            separator = text;
            invocation = invocation.ArgumentList.Arguments[1].Expression as InvocationExpressionSyntax;
        }

        // Sql.JsonArray(g.Select(x => col)): the selected values as one JSON array.
        ITypeSymbol? jsonElement = null;
        if (invocation is { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "JsonArray" } } && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType.Name: "Sql", TypeArguments.Length: 1 } jsonArray && invocation.ArgumentList.Arguments.Count == 1)
        {
            jsonElement = jsonArray.TypeArguments[0];
            invocation = invocation.ArgumentList.Arguments[0].Expression as InvocationExpressionSyntax;
        }

        // The chain on the group, outermost first: a terminal (Count, Sum…) or a Select, over Where/Select/Distinct steps.
        var steps = new List<(string Name, LambdaExpressionSyntax? Lambda)>();
        var node = invocation;
        while (node is { Expression: MemberAccessExpressionSyntax access })
        {
            var lambda = node.ArgumentList.Arguments.Count == 1 ? node.ArgumentList.Arguments[0].Expression as LambdaExpressionSyntax : null;
            if (node.ArgumentList.Arguments.Count > 1 || (node.ArgumentList.Arguments.Count == 1 && lambda is null))
            {
                error = "an aggregate of a group takes a lambda over the row, or nothing";
                return null;
            }

            steps.Insert(0, (access.Name.Identifier.ValueText, lambda));
            if (LambdaTranslator.Unwrap(access.Expression) is IdentifierNameSyntax root && model.GetSymbolInfo(root).Symbol is IParameterSymbol p && SymbolEqualityComparer.Default.Equals(p, group))
                break;
            node = LambdaTranslator.Unwrap(access.Expression) as InvocationExpressionSyntax;
        }

        if (steps.Count == 0 || node is null)
        {
            error = "the aggregates of a group are Count(), Count(pred), Sum/Min/Max/Average(x => col), each after a Where(pred) or a Select(col).Distinct(), and string.Join(sep, g.Select(x => col))";
            return null;
        }

        string? predicate = null;
        string? selected = null;
        var distinct = false;
        string? terminal = null;
        LambdaExpressionSyntax? terminalLambda = null;
        foreach (var (name, lambda) in steps)
        {
            switch (name)
            {
                case "Where" when lambda is not null:
                {
                    var text = Translate(lambda, translate, piece, asPredicate: true, out error);
                    if (text is null)
                        return null;
                    predicate = predicate is null ? text : $"({predicate}) AND ({text})";
                    break;
                }

                case "Select" when lambda is not null:
                    selected = Translate(lambda, translate, piece, asPredicate: false, out error);
                    if (selected is null)
                        return null;
                    break;
                case "Distinct" when lambda is null:
                    distinct = true;
                    break;
                case "Count" or "Sum" or "Min" or "Max" or "Average":
                    terminal = name;
                    terminalLambda = lambda;
                    break;
                default:
                    error = $"'{name}' is not an aggregate step of a group (Where, Select, Distinct, then Count/Sum/Min/Max/Average)";
                    return null;
            }
        }

        if (jsonElement is not null)
        {
            if (selected is null || terminal is not null || predicate is not null || distinct)
            {
                error = "Sql.JsonArray over a group takes g.Select(x => col) alone (no Where or Distinct before it: some dialects keep the rows they leave out as nulls)";
                return null;
            }

            kind = Kind.Text;
            return JsonArray(selected, jsonElement, piece, out error);
        }

        if (separator is not null)
        {
            // STRING_AGG of the selected column (DISTINCT where PostgreSQL has it; SQLite's group_concat takes it too, SQL Server does not).
            if (selected is null || terminal is not null)
            {
                error = "string.Join over a group takes g.Select(x => col), with a Where before it";
                return null;
            }

            kind = Kind.Text;
            var value = predicate is null ? selected : $"CASE WHEN {predicate} THEN {selected} END";
            var literal = "'" + separator.Replace("'", "''") + "'";
            if (distinct)
                return piece(new DialectPiece($"group_concat(DISTINCT {value}, {literal})", $"STRING_AGG(DISTINCT {value}, {literal})", $"STRING_AGG({value}, {literal})", $"GROUP_CONCAT(DISTINCT {value} SEPARATOR {literal})", $"LISTAGG(DISTINCT {value}, {literal}) WITHIN GROUP (ORDER BY NULL)"));
            return piece(new DialectPiece($"group_concat({value}, {literal})", $"STRING_AGG({value}, {literal})", $"STRING_AGG({value}, {literal})", $"GROUP_CONCAT({value} SEPARATOR {literal})", $"LISTAGG({value}, {literal}) WITHIN GROUP (ORDER BY NULL)"));
        }

        if (terminal is null)
        {
            error = "an aggregate of a group ends in Count, Sum, Min, Max or Average";
            return null;
        }

        if (terminal == "Count")
        {
            kind = Kind.Count;
            if (terminalLambda is not null)
            {
                var text = Translate(terminalLambda, translate, piece, asPredicate: true, out error);
                if (text is null)
                    return null;
                predicate = predicate is null ? text : $"({predicate}) AND ({text})";
            }

            if (distinct)
            {
                if (selected is null)
                {
                    error = "COUNT(DISTINCT …) is g.Select(x => col).Distinct().Count()";
                    return null;
                }

                return predicate is null ? $"COUNT(DISTINCT {selected})" : $"COUNT(DISTINCT CASE WHEN {predicate} THEN {selected} END)";
            }

            return predicate is null ? "COUNT(*)" : $"COUNT(CASE WHEN {predicate} THEN 1 END)";
        }

        // Sum/Min/Max/Average: the value from the terminal's lambda, or from a Select before it.
        var column = terminalLambda is not null ? Translate(terminalLambda, translate, piece, asPredicate: false, out error) : selected;
        if (column is null)
        {
            error ??= $"{terminal} takes a lambda selecting the value (g.{terminal}(x => x.Col))";
            return null;
        }

        kind = terminal == "Average" ? Kind.Average : Kind.Value;
        var function = terminal.ToUpperInvariant().Replace("AVERAGE", "AVG");
        var argument = predicate is null ? column : $"CASE WHEN {predicate} THEN {column} END";
        if (kind == Kind.Average && model.GetSymbolInfo(invocation!).Symbol is IMethodSymbol average && AveragedType(average) is { } averaged && EntityModel.IsInteger(averaged))
            argument += " * 1E0"; // LINQ's mean of integers is a double: SQL Server's AVG over them an integer
        return $"{function}({(distinct ? "DISTINCT " : "")}{argument})";
    }

    /// <summary>
    ///     The group's values as one JSON array (text): the dialect's aggregate where it has one; on SQL Server (JSON_ARRAYAGG only from 2025)
    ///     the elements encoded by type and joined with STRING_AGG (the quote as NCHAR(34): a double quote in the text is an identifier's, which the
    ///     dialect rewrite turns into brackets). Strings, Guids, integers, decimals and booleans; a null element is null.
    /// </summary>
    private static string? JsonArray(string value, ITypeSymbol element, Func<DialectPiece, string> piece, out string? error)
    {
        error = null;
        var type = EntityModel.Unwrap(element);
        var sqlServerElement = type.SpecialType switch
        {
            SpecialType.System_String => $"NCHAR(34) + STRING_ESCAPE({value}, 'json') + NCHAR(34)",
            SpecialType.System_Boolean => $"CASE WHEN {value} = 1 THEN 'true' ELSE 'false' END",
            _ when EntityModel.IsInteger(type) || type.SpecialType == SpecialType.System_Decimal => $"CONVERT(nvarchar(max), {value})",
            _ when type.ToDisplayString() == "System.Guid" => $"NCHAR(34) + LOWER(CONVERT(nvarchar(36), {value})) + NCHAR(34)",
            _ => null,
        };
        if (sqlServerElement is null)
        {
            error = $"Sql.JsonArray takes strings, Guids, integers, decimals or booleans, not {type.ToDisplayString()}";
            return null;
        }

        var sqlServer = $"'[' + STRING_AGG(CAST(COALESCE({sqlServerElement}, 'null') AS nvarchar(max)), ',') + ']'";
        return piece(new DialectPiece($"json_group_array({value})", $"CAST(json_agg({value}) AS text)", sqlServer, $"JSON_ARRAYAGG({value})", $"JSON_ARRAYAGG({value} NULL ON NULL RETURNING CLOB)"));
    }

    /// <summary>The type an Average reads: its selector's result, or the element of the sequence it is called on (after a Select).</summary>
    private static ITypeSymbol? AveragedType(IMethodSymbol average)
    {
        if (average.Parameters.Length == 1 && average.Parameters[0].Type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke })
            return invoke.ReturnType;
        var parameters = (average.ReducedFrom ?? average).Parameters;
        return parameters.Length > 0 && parameters[0].Type is INamedTypeSymbol { TypeArguments.Length: 1 } sequence ? sequence.TypeArguments[0] : null;
    }

    /// <summary>True when the expression is an aggregate chain on the group (<c>g.…</c>) or <c>string.Join</c> over one.</summary>
    public static bool IsOnGroup(ExpressionSyntax expression, SemanticModel model, IParameterSymbol group)
    {
        var node = expression as InvocationExpressionSyntax;
        if (node is { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Join" } } && node.ArgumentList.Arguments.Count == 2 && model.GetSymbolInfo(node).Symbol is IMethodSymbol { ContainingType.SpecialType: SpecialType.System_String })
            node = node.ArgumentList.Arguments[1].Expression as InvocationExpressionSyntax;
        else if (node is { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "JsonArray" } } && node.ArgumentList.Arguments.Count == 1 && model.GetSymbolInfo(node).Symbol is IMethodSymbol { ContainingType.Name: "Sql" })
            node = node.ArgumentList.Arguments[0].Expression as InvocationExpressionSyntax;
        while (node is { Expression: MemberAccessExpressionSyntax access })
        {
            var receiver = LambdaTranslator.Unwrap(access.Expression);
            if (receiver is IdentifierNameSyntax root)
                return model.GetSymbolInfo(root).Symbol is IParameterSymbol p && SymbolEqualityComparer.Default.Equals(p, group);
            node = receiver as InvocationExpressionSyntax;
        }

        return false;
    }

    private static string? Translate(LambdaExpressionSyntax lambda, Func<LambdaExpressionSyntax, LambdaTranslator?> translate, Func<DialectPiece, string> piece, bool asPredicate, out string? error)
    {
        error = null;
        if (lambda.ExpressionBody is null)
        {
            error = "an aggregate's lambda takes the row and has an expression body";
            return null;
        }

        var translator = translate(lambda);
        if (translator is null)
        {
            error = "an aggregate's lambda takes one parameter, the row";
            return null;
        }

        translator.InlineConstants = true; // an aggregate is a column of the select list: its constants are literals, never parameters
        var text = asPredicate ? translator.Predicate(lambda.ExpressionBody) : translator.ColumnOf(lambda.ExpressionBody);
        if (text is null || translator.ParameterPaths.Count > 0 || translator.Joins.Count > 0)
        {
            error = translator.Error ?? "the aggregate reads columns of the grouped row (through a navigation already joined before the GroupBy) and constants";
            return null;
        }

        // The lambda's own dialect pieces (a boolean literal, a catalog function) handed to the caller's list, the tokens renumbered.
        return PieceToken.Replace(text, m => piece(translator.DialectPieces[int.Parse(m.Groups[1].Value)]));
    }

    private static readonly System.Text.RegularExpressions.Regex PieceToken = new(@"\{d(\d+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);
}
