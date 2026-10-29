using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Schema;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>The command terminals: <c>ExecuteUpdateAsync</c> (a SET list over the filtered rows) and <c>ExecuteDeleteAsync</c>, one statement each.</summary>
internal sealed partial class PlanBuilder
{
    private readonly List<string> _sets = []; // `"Column" = @pN` or `"Column" = <expression>`, in source order

    public bool IsCommand => _terminal is "ExecuteUpdateAsync" or "ExecuteDeleteAsync" or "ExecuteInsertAsync" or "SyncAsync";

    /// <summary>
    ///     The SET list of an update: <c>s => s.SetProperty(o => o.Col, constant).SetProperty(o => o.Other, o => o.Other + 1)</c>, or with
    ///     the caller's values as the argument, <c>(s, a) => s.SetProperty(o => o.Col, a.Value)</c>. A value is a constant (a field of the
    ///     plan), a member of the argument in the chain's slot, or the value itself in a [Query]; an expression over the row is
    ///     translated, with no values in it but numeric literals. False with a diagnostic.
    /// </summary>
    public bool AddSet(SemanticModel model, LambdaExpressionSyntax lambda, Location where, List<Diagnostic> diagnostics)
    {
        void Report(string expression, string reason) => diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, expression, "set", reason));
        if (!SqlInterceptorGenerator.TryParameters(model, lambda, out var set, out var arg))
        {
            Report(lambda.ToString(), "the SET list is a lambda: s => s.SetProperty(o => o.Column, constant), or (s, a) => s.SetProperty(o => o.Column, a.Value) with the values as the argument");
            return false;
        }

        if (arg is not null && _valuesInSlots)
        {
            Report(lambda.ToString(), "a [Query] definition writes the one-parameter form; its own parameters are the values");
            return false;
        }

        // The chain of SetProperty calls, outermost first, down to the lambda parameter.
        var calls = new List<InvocationExpressionSyntax>();
        var node = SqlInterceptorGenerator.Body(lambda);
        while (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SetProperty" } access } call)
        {
            calls.Add(call);
            node = access.Expression;
        }

        if (calls.Count == 0 || node is not IdentifierNameSyntax root || model.GetSymbolInfo(root).Symbol is not IParameterSymbol p || !SymbolEqualityComparer.Default.Equals(p, set))
        {
            Report(lambda.ToString(), "the SET list is a chain of SetProperty calls on the lambda parameter");
            return false;
        }

        var argVariable = arg is null ? null : $"arg{_slot}";
        if (arg is not null)
            _binds.Add($"var {argVariable} = ({arg.Type.ToDisplayString(SqlInterceptorGenerator.Annotated)})q.Slot({_slot})!;");
        var values = new LambdaTranslator(model, _entities, _entity, set, null, arg, argVariable, inline: !_valuesInSlots);
        var sets = new List<string>();
        // Recorded (a bulk update's): the k-th value call, in source order, is the k-th value the delegate records when it runs.
        var valueCalls = calls.Count(c => c.ArgumentList.Arguments.Count == 2 && c.ArgumentList.Arguments[1].Expression is not LambdaExpressionSyntax);
        var seenValues = 0;
        foreach (var call in calls)
        {
            var arguments = call.ArgumentList.Arguments;
            if (arguments.Count != 2 || arguments[0].Expression is not LambdaExpressionSyntax property || !SqlInterceptorGenerator.TryParameter(model, property, out var row))
            {
                Report(call.ToString(), "SetProperty takes the column (o => o.Column) and a value, or an expression over the row (o => o.Column + 1)");
                return false;
            }

            var columnTranslator = new LambdaTranslator(model, _entities, _entity, row);
            var column = columnTranslator.ColumnOf(SqlInterceptorGenerator.Body(property));
            var json = columnTranslator.LastJson; // a member of a JSON column: the column is set to the document with that member replaced
            if (column is null || columnTranslator.ParameterPaths.Count > 0 || columnTranslator.Joins.Count > 0 || (json is null && !column.StartsWith("t.\"", System.StringComparison.Ordinal)))
            {
                Report(property.ToString(), columnTranslator.Error ?? "the property must be a column of the entity, or a member of a JSON column");
                return false;
            }

            string text;
            if (arguments[1].Expression is LambdaExpressionSyntax expression)
            {
                if (!SqlInterceptorGenerator.TryParameter(model, expression, out var row2))
                {
                    Report(expression.ToString(), "the expression is a lambda with one parameter");
                    return false;
                }

                var valueTranslator = new LambdaTranslator(model, _entities, _entity, row2);
                var translated = valueTranslator.ColumnOf(SqlInterceptorGenerator.Body(expression));
                // The expression has no tree to read values from (it is a Func): numeric literals are written into the statement, anything else captured is refused.
                if (translated is not null && valueTranslator.ParameterPaths.Count > 0)
                    translated = Inline(translated, valueTranslator.ParameterTexts);
                if (translated is null)
                {
                    Report(expression.ToString(), valueTranslator.Error ?? "the expression uses the row's columns and numeric literals only; a captured value goes in the value form, SetProperty(o => o.Column, value)");
                    return false;
                }

                Merge(_joins, valueTranslator.Joins); // a value from a navigation: the joined row (UPDATE … FROM)
                _innerAliases.UnionWith(valueTranslator.InnerAliases);
                text = translated;
            }
            else if (_recordedValues)
            {
                // The value is the delegate's, whatever it captures: a slot filled from what it records (BulkSetterValues).
                var index = _kinds.Count;
                ValueSlots.Add($"__values[{valueCalls - 1 - seenValues++}]");
                _binds.Add($"p[{index}].Value = global::Pragmatic.Persistence.Sql.Planning.SqlValue.ToDatabase(d, q.Slot({ValueSlots.Count - 1}));");
                var valueType = model.GetTypeInfo(arguments[1].Expression);
                _kinds.Add(LambdaTranslator.KindOf(valueType.ConvertedType ?? valueType.Type)); // the column's type: a null literal has none of its own
                text = "@p" + index;
            }
            else
            {
                var valueText = values.ValueText(arguments[1].Expression, out var isConstant);
                if (valueText is null)
                {
                    Report(arguments[1].ToString(), values.Error ?? "the value must be a constant or a member of the argument");
                    return false;
                }

                var index = _kinds.Count;
                string value;
                if (isConstant)
                    value = ConstantField(valueText);
                else if (_valuesInSlots)
                {
                    ValueSlots.Add(valueText);
                    value = $"q.Slot({ValueSlots.Count - 1})";
                }
                else
                    value = valueText; // argN.Member

                _binds.Add($"p[{index}].Value = global::Pragmatic.Persistence.Sql.Planning.SqlValue.ToDatabase(d, {value});");
                _kinds.Add(LambdaTranslator.KindOf(model.GetTypeInfo(arguments[1].Expression).Type));
                text = "@p" + index;
            }

            if (json is { } member)
            {
                // json_set / jsonb_set / JSON_MODIFY of the document at the member's path: one piece per dialect, resolved with the command.
                var jsonSet = SqlCatalog.Find("JsonSet", 3)!;
                _dialectPieces.Add(new DialectPiece(jsonSet.Render(SqlDialect.Sqlite, [member.Column, member.Path, text]), jsonSet.Render(SqlDialect.PostgreSql, [member.Column, member.Path, text]), jsonSet.Render(SqlDialect.SqlServer, [member.Column, member.Path, text]), jsonSet.Render(SqlDialect.MySql, [member.Column, member.Path, text]), jsonSet.Render(SqlDialect.Oracle, [member.Column, member.Path, text])));
                sets.Add($"{member.Column.Substring(2)} = {{d{_dialectPieces.Count - 1}}}");
                continue;
            }

            sets.Add($"{column.Substring(2)} = {text}"); // `t."Column"` → `"Column"`: a SET names the column bare
        }

        sets.Reverse(); // source order
        _sets.AddRange(sets);
        if (arg is not null)
            _slot++;
        return true;
    }

    private static readonly Regex NumericLiteral = new(@"^(\d+(\.\d+)?)[mMdDfFlLuU]*$", RegexOptions.Compiled);

    /// <summary>Numeric literals of a SET expression written into the fragment in place of their placeholders; null when a captured value is not a numeric literal.</summary>
    private static string? Inline(string fragment, IReadOnlyList<string> texts)
    {
        var literals = new string[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            var match = NumericLiteral.Match(texts[i].Trim());
            if (!match.Success)
                return null;
            literals[i] = match.Groups[1].Value;
        }

        return Placeholder.Replace(fragment, m => literals[int.Parse(m.Groups[1].Value)]);
    }

    /// <summary>A set-based command on rows that feed [RollUp] aggregates: it would leave them stale.</summary>
    private static string RollUpRefusal(EntityModel entity)
        => $"{entity.Type.Name} feeds [RollUp] aggregates ({string.Join(", ", entity.RollUpParents)}): a set-based command would leave them stale; write its rows (InsertAsync, UpdateAsync, DeleteAsync, SaveAsync, a write batch), which keep them";

    /// <summary>A command takes a filtered chain of the entity and nothing else; the reason when it does not.</summary>
    private string? CommandProblem()
    {
        if (_projection is not null || _groupKey is not null)
            return "a command works on the entity's rows, not on a projection or on groups";
        if (_raw is not null || _rawFrom is not null)
            return "a command works on the entity's table, not on a hand-written statement or a function";
        if (_entity.ReadOnly)
            return $"{_entity.Type.Name} is read-only ([Table(ReadOnly = true)], a view): it is read, never written";
        if (_entity.Tpc && _entity.IsHierarchyRoot)
            return $"{_entity.Type.Name} is the root of a table-per-concrete-type hierarchy, a view over the types' tables: update or delete through a derived type (OfType<T>() or Query<T>())";
        if (_includes.Count > 0 || _orderings.Count > 0 || _hasSkip || _hasTake)
            return "a command takes no Include, ordering or paging";
        if (_entity.RollUpParents.Count > 0 && _terminal != "ExecuteInsertAsync")
            return RollUpRefusal(_entity);
        if (_terminal == "ExecuteUpdateAsync" && _sets.Count == 0)
            return "the update sets nothing";
        if (_terminal == "ExecuteDeleteAsync" && _entity.SoftDeleteColumn is not null && !_withoutFilters && !_erasure)
            return $"{_entity.Type.Name} is [SoftDelete]: a set-based delete would remove the rows; mark them with ExecuteUpdateAsync, or purge with WithoutFilters()";
        if (_terminal is "ExecuteUpdateAsync" or "ExecuteDeleteAsync" && _joins.Any(j => j.StartsWith("LEFT JOIN", System.StringComparison.Ordinal) && !_innerAliases.Contains(JoinAlias(j))))
            return "an optional navigation in the predicate of an update or delete: the joined row may be missing; filter on a required navigation, on the foreign key, or test the navigation for null";
        if (_terminal == "SyncAsync")
        {
            if (_joins.Count > 0)
                return "SyncAsync names the set with predicates on the entity's own columns (a MERGE joins nothing): filter on the foreign key";
            if (_entity.Keys.Count != 1)
                return $"SyncAsync matches rows on one key column; {_entity.Type.Name} has {(_entity.Keys.Count == 0 ? "none" : "a composite key")}";
            if (_entity.Derived is not null || _entity.FixedPredicate is not null)
                return "SyncAsync works on an entity with a table of its own, not on a hierarchy's types";
            if (_entity.Identity)
                return "SyncAsync matches rows on their key: a key the database assigns cannot be matched before the insert";
        }

        return null;
    }
}
