using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Schema;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>The method calls a lambda may contain: string functions, LIKE, IN lists, the aggregates of a group, and a method on captured values.</summary>
internal sealed partial class LambdaTranslator
{
    private string? Invocation(InvocationExpressionSyntax invocation, string path)
    {
        if (_model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return Fail(invocation, "the method could not be resolved");
        if (method.ContainingType.ToDisplayString() == "Pragmatic.Persistence.Sql.Sql")
            return Catalog(invocation, method, path); // before the capture test: Sql.Now() reads no parameter and is still SQL
        if (method.GetAttributes().Any(a => a.AttributeClass?.Name == "SqlFunctionAttribute"))
            return Application(invocation, method, path);
        if (method.Name == "Any" && method.ContainingType.ToDisplayString() == "System.Linq.Enumerable" && invocation.Expression is MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax jsonObjects } && invocation.ArgumentList.Arguments.Count == 1
            && invocation.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax anyLambda && IsJsonArray(jsonObjects))
            return JsonArrayAny(invocation, jsonObjects, anyLambda, path); // an element of a JSON array of objects meeting a condition
        if (method.Name is "Count" or "Any" && method.ContainingType.ToDisplayString() == "System.Linq.Enumerable" && JsonArrayQuery(invocation, method, path) is { } jsonQuery)
            return jsonQuery; // Count(e => …), Where(e => …).Any()/Count(), Distinct().Count() over a JSON array
        if (method.Name == "Any" && invocation.Expression is MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax overlapped } && invocation.ArgumentList.Arguments.Count == 1 && IsArrayColumn(overlapped))
            return ArrayOverlaps(invocation, overlapped, path); // x.Tags.Any(t => list.Contains(t)) on an array column: an element in common with a list of the caller's
        if (method.Name is "Any" or "All" or "Count" && method.ContainingType.ToDisplayString() == "System.Linq.Enumerable" && _groupKey is null && !IsCapturedValue(invocation))
            return CollectionPredicate(invocation, method, path); // o.Lines.Any(l => …): a correlated subquery on the child's table
        if (method.ContainingType.OriginalDefinition.ToDisplayString() == "Pragmatic.Persistence.Sql.SqlQuery<T>" && method.Name is "Any" or "Count" or "Min" or "Max" or "Sum" or "Average" or "Contains")
            return Subquery(invocation, method, path); // a chain written here, ending in a scalar: a correlated subquery of the statement
        if (IsCapturedValue(invocation))
            return Parameter(path, invocation); // a method on captured values (text.ToLower(), id.ToString()): computed in .NET, sent as a value
        var target = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        if (_groupKey is not null && GroupAggregate.IsOnGroup(invocation, _model, _parameter))
            return Aggregate(invocation);
        if (method.ContainingType.ToDisplayString() == "Pragmatic.Persistence.Sql.Functions.SqlDateRange" && method.Name == "Contains" && target is not null && invocation.ArgumentList.Arguments.Count == 1)
            return RangeContains(invocation, target, path);
        if (method.Name == "Contains" && target is MemberAccessExpressionSyntax jsonArray && invocation.ArgumentList.Arguments.Count == 1 && IsJsonArray(jsonArray))
            return JsonArrayContains(invocation, jsonArray, invocation.ArgumentList.Arguments[0].Expression, path); // an element of a JSON array
        if (method.Name == "Contains" && target is MemberAccessExpressionSyntax arrayColumn && invocation.ArgumentList.Arguments.Count == 1 && IsArrayColumn(arrayColumn))
            return ArrayContains(arrayColumn, invocation.ArgumentList.Arguments[0].Expression, path); // an element of an array column

        // string methods on a column
        if (method.ContainingType.SpecialType == SpecialType.System_String && target is not null)
        {
            switch (method.Name)
            {
                case "Contains" or "StartsWith" or "EndsWith" when invocation.ArgumentList.Arguments.Count == 1:
                {
                    var column = Translate(target, $"Captured.Target({path})", asPredicate: false);
                    if (column is null)
                        return null;
                    var argument = invocation.ArgumentList.Arguments[0].Expression;
                    if (!IsCapturedValue(argument) && argument is not LiteralExpressionSyntax)
                        return Fail(argument, "the text of a LIKE must be a value, not a column");
                    var (prefix, suffix) = method.Name switch { "Contains" => ("%", "%"), "StartsWith" => ("", "%"), _ => ("%", "") };
                    if (prefix == "%" && LastColumn is { } indexed && (indexed.IsIndexed || indexed.IsKey) && RootColumns.Contains(indexed))
                        LeadingWildcards.Add((indexed.Property, method.Name)); // the index on the column is of no use to a pattern that starts with %
                    var text = ValueText(argument, out var isConstant);
                    if (text is null)
                        return null;
                    // A constant pattern is one string; otherwise the wildcards are added around the value when it is bound.
                    var pattern = isConstant && _model.GetConstantValue(argument).Value is string constant ? ConstantText(prefix + constant + suffix, null) : $"\"{prefix}\" + ({text}) + \"{suffix}\"";
                    Add($"Captured.Like(Captured.Argument({path}, 0), \"{prefix}\", \"{suffix}\")", "String", pattern, isConstant);
                    return $"{column} LIKE {{{_parameterPaths.Count - 1}}}";
                }

                case "ToLower" or "ToLowerInvariant" when invocation.ArgumentList.Arguments.Count == 0:
                {
                    var column = Translate(target, $"Captured.Target({path})", asPredicate: false);
                    return column is null ? null : $"LOWER({column})";
                }

                case "ToUpper" or "ToUpperInvariant" when invocation.ArgumentList.Arguments.Count == 0:
                {
                    var column = Translate(target, $"Captured.Target({path})", asPredicate: false);
                    return column is null ? null : $"UPPER({column})";
                }

                case "IsNullOrEmpty" when method.IsStatic && invocation.ArgumentList.Arguments.Count == 1:
                {
                    var column = Translate(invocation.ArgumentList.Arguments[0].Expression, $"Captured.Argument({path}, 0)", asPredicate: false);
                    return column is null ? null : $"({column} IS NULL OR {column} = '')";
                }
            }
        }

        // collection.Contains(column) → IN. Three shapes: an instance method (List<T>.Contains: the list is the
        // call's Object), an extension written as instance (array.Contains(x): reduced, the list is argument 0 in
        // the tree), and a static call (Enumerable.Contains(list, x)).
        if (method.Name == "Contains" && invocation.ArgumentList.Arguments.Count >= 1)
        {
            var isExtension = method.ReducedFrom is not null;
            var isStatic = method.IsStatic && !isExtension;
            var collection = isStatic ? invocation.ArgumentList.Arguments[0].Expression : target;
            var item = isStatic ? invocation.ArgumentList.Arguments.Count > 1 ? invocation.ArgumentList.Arguments[1].Expression : null : invocation.ArgumentList.Arguments[0].Expression;
            if (collection is not null && item is not null && IsCapturedValue(collection))
            {
                var inTree = isStatic || isExtension; // the collection is argument 0 of the tree's call, the item argument 1
                var itemPath = inTree ? $"Captured.Argument({path}, 1)" : $"Captured.Argument({path}, 0)";
                var column = Translate(item, itemPath, asPredicate: false);
                if (column is null)
                    return null;
                var collectionPath = inTree ? $"Captured.Argument({path}, 0)" : $"Captured.Target({path})";
                var text = ValueText(collection, out _);
                if (text is null)
                    return null;
                Add($"Captured.List({collectionPath})", "List", text, isConstant: false);
                // IN with the list expanded per call (one placeholder per item) on SQLite and SQL Server; an array parameter on PostgreSQL (= ANY: one text, one prepared shape).
                var slot = $"{{{_parameterPaths.Count - 1}}}";
                DialectPieces.Add(new DialectPiece($"{column} IN ({slot})", $"{column} = ANY({slot})", $"{column} IN ({slot})", $"{column} IN ({slot})", $"{column} IN ({slot})"));
                return "{d" + (DialectPieces.Count - 1) + "}";
            }
        }

        // column.Equals(value), the typed overload (Guid.Equals(Guid), not Equals(object)): an equality, as EF Core reads it. Pragmatic's
        // templates write it ([RequireExists]: __row.PersistenceId.Equals(key)).
        if (method.Name == "Equals" && !method.IsStatic && target is not null && invocation.ArgumentList.Arguments.Count == 1
            && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, method.ContainingType))
        {
            var argument = invocation.ArgumentList.Arguments[0].Expression;
            _convertedColumn = ConvertedColumnOf(target) ?? ConvertedColumnOf(argument);
            var left = Translate(target, $"Captured.Target({path})", asPredicate: false);
            var right = Translate(argument, $"Captured.Argument({path}, 0)", asPredicate: false);
            _convertedColumn = null;
            return left is null || right is null ? null : $"{left} = {right}";
        }

        return Fail(invocation, $"method '{method.Name}' is not translated; compute it before the query or use a declared [Query]");
    }

    /// <summary>
    ///     A function of the typed catalog (<c>Sql.Year(o.CreatedAt)</c>): each argument translated (a column, a value, a literal; a
    ///     <c>DatePart</c> as its constant name), rendered for the three dialects by <c>SqlCatalog</c> and kept as a dialect piece the
    ///     plan resolves; an emulated rendering is noted for PRAG3012.
    /// </summary>
    private string? Catalog(InvocationExpressionSyntax invocation, IMethodSymbol method, string path)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var takesWindow = method.Parameters.Length > 0 && method.Parameters[method.Parameters.Length - 1].Type.Name == "SqlWindow";
        var entry = SqlCatalog.Find(takesWindow ? method.Name + "Window" : method.Name, arguments.Count); // over a named window: the entry that reads OVER it
        if (entry is null)
            return Fail(invocation, $"Sql.{method.Name} with {arguments.Count} arguments is not in the catalog");
        if (entry.IsWindow && !InlineConstants)
            return Fail(invocation, $"Sql.{method.Name} is a window function: a value of the select list or an ordering, not a predicate (SQL evaluates windows after the WHERE); to filter on it use DistinctOn, or a chain over the projection");
        if (entry.Name is "FullText" or "FullTextRank" && arguments.Count == 2)
            return FullText(invocation, method, entry, path);
        var isVector = entry.Name is "CosineDistance" or "L2Distance" or "InnerProduct";
        var rendered = new string[isVector ? arguments.Count + 1 : arguments.Count];
        ColumnModel? vectorColumn = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = Strip(arguments[i].Expression);
            if (i == entry.PartIndex)
            {
                if (_model.GetSymbolInfo(argument).Symbol is not IFieldSymbol { ContainingType.Name: "DatePart" } part)
                    return Fail(argument, "the date part must be a constant (DatePart.Day): the generator renders it into the statement");
                rendered[i] = part.Name;
                continue;
            }

            if (i == entry.LiteralIndex)
            {
                if (_model.GetConstantValue(argument) is not { HasValue: true, Value: int count } || count < 0)
                    return Fail(argument, $"Sql.{method.Name}'s count must be a non-negative constant: the generator writes it into the statement (a window frame takes no parameter)");
                rendered[i] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }

            var text = Translate(argument, $"Captured.Argument({path}, {i})", asPredicate: false);
            if (text is null)
                return null;
            rendered[i] = text;
            if (isVector && i == 0)
                vectorColumn = LastColumn;
        }

        if (isVector)
        {
            // The dimensions, from the column the distance is computed on: SQL Server casts the vector argument to VECTOR(n).
            if (vectorColumn?.VectorDimensions is not { } dimensions)
                return Fail(invocation, $"Sql.{method.Name} takes a [Vector(n)] column first: the dimensions type the other vector");
            rendered[arguments.Count] = dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Rendered(entry, rendered, $"Sql.{method.Name}");
    }

    /// <summary>
    ///     <c>Sql.FullText</c>/<c>FullTextRank</c>: on a <c>[FullText]</c> column the native entries (the column's table, name and key added: FTS5 on
    ///     SQLite, CONTAINS on SQL Server); on any other the plain ones, whose emulations are reported.
    /// </summary>
    private string? FullText(InvocationExpressionSyntax invocation, IMethodSymbol method, SqlCatalog.Entry entry, string path)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var column = Translate(Strip(arguments[0].Expression), $"Captured.Argument({path}, 0)", asPredicate: false);
        var marked = LastColumn;
        var query = column is null ? null : Translate(Strip(arguments[1].Expression), $"Captured.Argument({path}, 1)", asPredicate: false);
        if (column is null || query is null)
            return null;
        if (marked is not { FullTextTable: { } table, FullTextKey: { } key })
            return Rendered(entry, [column, query], $"Sql.{method.Name}");
        if (query.Length > 2 && query[0] == '{' && query[query.Length - 1] == '}' && int.TryParse(query.Substring(1, query.Length - 2), out var index))
        {
            // The query's parameter carried as a SqlFullTextQuery: bound as the engine's condition, every word required (a literal is turned in the catalog).
            _parameterTexts[index] = $"global::Pragmatic.Persistence.Sql.Functions.SqlFullTextQuery.Of({_parameterTexts[index]})";
            _parameterPaths[index] = $"SqlFullTextQuery.Of(global::Pragmatic.Persistence.Expressions.{_parameterPaths[index]})";
        }

        return Rendered(SqlCatalog.Find(entry.Name + "Native", 5)!, [column, query, table, marked.Column, key], $"Sql.{method.Name}");
    }

    /// <summary>A catalog entry rendered for every dialect as a dialect piece the plan resolves, its emulations noted under <paramref name="label" /> (PRAG3012).</summary>
    private string Rendered(SqlCatalog.Entry entry, string[] rendered, string label)
    {
        var piece = new DialectPiece(entry.Render(SqlDialect.Sqlite, rendered), entry.Render(SqlDialect.PostgreSql, rendered), entry.Render(SqlDialect.SqlServer, rendered), entry.Render(SqlDialect.MySql, rendered), entry.Render(SqlDialect.Oracle, rendered));
        DialectPieces.Add(piece);
        LastColumn = null; // the result is a value of the function's type, not the column it was computed from (a projection reads it with the member's own reader)
        foreach (var dialect in SqlDialects.All)
        {
            if (entry.Emulation(dialect) is { } note)
                Emulations.Add((label, dialect, note));
        }

        return "{d" + (DialectPieces.Count - 1) + "}";
    }

    /// <summary>
    ///     <c>x.Range.Contains(day)</c> on an owned <c>SqlDateRange</c>: its two bound columns (the receiver's path with From and To appended; no
    ///     synthesized syntax, which would detach the nodes from the semantic model's tree) and the day, through the catalog's range entry.
    /// </summary>
    private string? RangeContains(InvocationExpressionSyntax invocation, ExpressionSyntax receiver, string path)
    {
        if (Strip(receiver) is not MemberAccessExpressionSyntax member || !IsOnParameter(member) || Segments(member, out var casts) is not { } segments)
            return Fail(invocation, "a range is a member of the row (x.Active.Contains(day))");
        var from = ColumnAt([.. segments, "From"], [.. casts, null], member);
        var to = ColumnAt([.. segments, "To"], [.. casts, null], member);
        var day = Translate(Strip(invocation.ArgumentList.Arguments[0].Expression), $"Captured.Argument({path}, 0)", asPredicate: false);
        if (from is null || to is null || day is null)
            return null;
        return Rendered(SqlCatalog.Find("RangeContains", 3)!, [from, to, day], "SqlDateRange.Contains");
    }

    /// <summary>An application function (<c>[SqlFunction]</c>): its definition read into a template per dialect, the arguments translated and put in place; a dialect without an arm fails here, with the remedy.</summary>
    private string? Application(InvocationExpressionSyntax invocation, IMethodSymbol method, string path)
    {
        var function = AppFunction.TryRead(method, _model.Compilation, out var problem);
        if (function is null)
            return Fail(invocation, $"[SqlFunction] {method.Name}: {problem}");
        var arguments = invocation.ArgumentList.Arguments;
        var rendered = new string[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            var text = Translate(Strip(arguments[i].Expression), $"Captured.Argument({path}, {i})", asPredicate: false);
            if (text is null)
                return null;
            rendered[i] = text;
        }

        var texts = new string[SqlDialects.All.Length];
        foreach (var dialect in SqlDialects.All)
        {
            texts[(int)dialect] = function.Render(dialect, rendered) ?? "";
            if (function.Template(dialect) is null)
                return Fail(invocation, $"{method.Name} has no rendering for {dialect}: add an arm 'SqlDialect.{dialect} => …' (or a discard) to its definition");
            if (function.Note(dialect) is { } note)
                Emulations.Add(($"{method.ContainingType.Name}.{method.Name}", dialect, note));
        }

        DialectPieces.Add(new DialectPiece(texts[0], texts[1], texts[2], texts[3], texts[(int)SqlDialect.Oracle]));
        LastColumn = null;
        return "{d" + (DialectPieces.Count - 1) + "}";
    }

    /// <summary>An aggregate of the group (<c>g.Count()</c>, <c>g.Sum(x => x.Col)</c>, Min, Max, Average): the same text the grouped Select uses, so HAVING and ORDER BY name the same expressions.</summary>
    /// <summary>An aggregate of the group in a HAVING or an ORDER BY: the same forms as in the grouped projection.</summary>
    private string? Aggregate(InvocationExpressionSyntax invocation)
    {
        var sql = GroupAggregate.Render(invocation, _model, _parameter, lambda => SqlInterceptorGenerator.TryParameter(_model, lambda, out var row) ? new LambdaTranslator(_model, _entities, _row, row) : null, piece =>
        {
            DialectPieces.Add(piece);
            return "{d" + (DialectPieces.Count - 1) + "}";
        }, out _, out var error);
        LastColumn = null;
        return sql ?? Fail(invocation, error ?? "the aggregate is outside the translated subset");
    }
}
