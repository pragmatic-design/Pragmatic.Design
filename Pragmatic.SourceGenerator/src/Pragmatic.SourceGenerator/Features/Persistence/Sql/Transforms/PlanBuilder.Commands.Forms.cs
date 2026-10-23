using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The forms of the command statements per dialect: an UPDATE or DELETE whose predicate joins a navigation (<c>UPDATE … FROM</c>,
///     <c>DELETE … USING</c>, <c>DELETE t FROM … JOIN</c>, an EXISTS where the dialect has none), the rows read back (<c>RETURNING</c>,
///     <c>OUTPUT INSERTED/DELETED</c>), <c>INSERT INTO … SELECT</c> from the chain's rows, and the MERGE of a set with given rows
///     (<c>SyncAsync</c>: the rows' VALUES left to the runtime as <c>{rows}</c>, the keys as <c>{keys}</c>).
/// </summary>
internal sealed partial class PlanBuilder
{
    private static readonly Regex JoinShape = new("^(INNER|LEFT) JOIN \"([^\"]+)\" (\\S+) ON (.+)$", RegexOptions.Compiled);

    private SqlInterceptorGenerator.Projection? _returning; // the rows read back by an update or delete; Construct empty for the whole row
    private string? _insertTable; // INSERT … SELECT: the target table and its columns, the chain's expressions as the values
    private List<string>? _insertColumns;
    private List<string>? _insertValues;

    private const string OracleReturning = "Oracle's RETURNING … INTO fills variables, not a result set: this call fails there; read the rows first, then write";

    private static string JoinAlias(string join) => JoinShape.Match(join) is { Success: true } m ? m.Groups[3].Value : "";

    /// <summary>The <c>returning</c> lambda of an update or delete: the row itself (<c>o => o</c>), or a projection over its own columns.</summary>
    public bool AddReturning(SemanticModel model, LambdaExpressionSyntax lambda, ITypeSymbol resultType, Location where, List<Diagnostic> diagnostics)
    {
        void Report(string expression, string reason) => diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, expression, "returning", reason));
        if (!SqlInterceptorGenerator.TryParameter(model, lambda, out var parameter))
        {
            Report(lambda.ToString(), "the rows read back are a lambda over the row: o => o, or o => new Dto { … }");
            return false;
        }

        var body = SqlInterceptorGenerator.Body(lambda);
        if (body is IdentifierNameSyntax name && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, parameter))
        {
            _returning = SqlInterceptorGenerator.Projection.WholeRow(_entity);
            TerminalEmulations.Add(("returning", SqlDialect.MySql, "MySQL has no RETURNING or OUTPUT: this call fails there; read the rows first, then write"));
            TerminalEmulations.Add(("returning", SqlDialect.Oracle, OracleReturning));
            return true;
        }

        var translator = Bound(new LambdaTranslator(model, _entities, Row, parameter) { InlineConstants = true });
        var projection = SqlInterceptorGenerator.Projection.TryRead(model, lambda, resultType, translator, diagnostics, where);
        if (projection is null)
            return false;
        if (projection.IsAnonymous || translator.Joins.Count > 0 || translator.DialectPieces.Count > 0)
        {
            Report(lambda.ToString(), "the rows read back are the row's own columns, as the row or a DTO (no anonymous type, no navigation, no catalog function)");
            return false;
        }

        _returning = projection;
        TerminalEmulations.Add(("returning", SqlDialect.MySql, "MySQL has no RETURNING or OUTPUT: this call fails there; read the rows first, then write"));
        TerminalEmulations.Add(("returning", SqlDialect.Oracle, OracleReturning));
        return true;
    }

    /// <summary>The <c>rows</c> lambda of an INSERT … SELECT: an object initializer of the target entity over the chain's row.</summary>
    public bool AddInsertSelect(SemanticModel model, LambdaExpressionSyntax lambda, INamedTypeSymbol? targetType, Location where, List<Diagnostic> diagnostics)
    {
        void Report(string expression, string reason) => diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, expression, "rows", reason));
        var target = targetType is null ? null : _entities(targetType);
        if (target is null || target.IsRawShape)
        {
            Report(lambda.ToString(), "the target of an INSERT … SELECT is an [Entity]: ExecuteInsertAsync(o => new Target { … })");
            return false;
        }

        if (target.ReadOnly || (target.Tpc && target.IsHierarchyRoot) || target.Derived is not null || target.FixedPredicate is not null)
        {
            Report(lambda.ToString(), $"{target.Type.Name} is not written this way (a view, a hierarchy's root or a type of one): a table of its own takes the rows");
            return false;
        }

        if (!SqlInterceptorGenerator.TryParameter(model, lambda, out var parameter) || SqlInterceptorGenerator.Body(lambda) is not ObjectCreationExpressionSyntax)
        {
            Report(lambda.ToString(), "the rows are an object initializer of the target over the chain's row: o => new Target { Column = o.Column, Other = constant }");
            return false;
        }

        var translator = Bound(new LambdaTranslator(model, _entities, Row, parameter) { InlineConstants = true });
        var projection = SqlInterceptorGenerator.Projection.TryRead(model, lambda, targetType!, translator, diagnostics, where);
        if (projection is null)
            return false;
        if (projection.Members is null || projection.Members.Count != projection.Columns.Count)
        {
            Report(lambda.ToString(), "each member of the target takes one column or constant");
            return false;
        }

        var columns = new List<string>();
        foreach (var (member, _) in projection.Members)
        {
            var column = target.Column(member);
            if (column is null)
            {
                Report(member, $"'{member}' is not a column of {target.Type.Name}");
                return false;
            }

            if (target.Identity && column.IsKey || (target.Identity && target.Keys.Count == 1 && target.Keys[0] == column))
            {
                Report(member, $"'{member}' is the key the database assigns: leave it out");
                return false;
            }

            columns.Add(column.Column);
        }

        var values = projection.Columns;
        if (translator.DialectPieces.Count > 0)
        {
            var offset = _dialectPieces.Count;
            values = new List<string>(values);
            values[0] = Absorb(translator, values[0], _kinds.Count);
            for (var i = 1; i < values.Count; i++)
                values[i] = PieceToken.Replace(values[i], m => "{d" + (offset + int.Parse(m.Groups[1].Value)) + "}");
        }

        if (target.RollUpParents.Count > 0)
        {
            Report(lambda.ToString(), RollUpRefusal(target));
            return false;
        }

        Merge(_joins, translator.Joins);
        _innerAliases.UnionWith(translator.InnerAliases);
        _insertTable = target.Table;
        _insertColumns = columns;
        _insertValues = values;
        return true;
    }

    /// <summary>The command statement in each dialect's form: the alias <c>t</c> the predicates use.</summary>
    private string CommandSql(SqlDialect dialect)
    {
        var sql = new StringBuilder();
        switch (_terminal)
        {
            case "ExecuteInsertAsync":
                sql.Append("INSERT INTO \"").Append(_insertTable).Append("\" (").Append(string.Join(", ", _insertColumns!.Select(c => "\"" + c + "\""))).Append(") SELECT ").Append(string.Join(", ", _insertValues!));
                sql.Append(" FROM \"").Append(_entity.Table).Append("\" t");
                foreach (var join in _joins)
                    sql.Append(' ').Append(Inner(join));
                AppendWhere(sql, []);
                break;
            case "SyncAsync":
                Sync(sql, dialect);
                break;
            case "ExecuteUpdateAsync":
                Update(sql, dialect);
                break;
            default:
                Delete(sql, dialect);
                break;
        }

        return ForDialect(Resolve(sql.ToString(), dialect), dialect); // the dialect pieces of the predicates and the sets (a catalog function, a JSON member) resolved as in a statement
    }

    private void AppendWhere(StringBuilder sql, IEnumerable<string> first)
    {
        var started = false;
        foreach (var condition in first.Concat(_predicates))
        {
            sql.Append(started ? " AND (" : " WHERE (").Append(condition).Append(')');
            started = true;
        }
    }

    /// <summary>The WHERE of a command whose dialect cannot join in it (SQLite's DELETE, Oracle's UPDATE and DELETE): the joined rows and every predicate inside an EXISTS over the row.</summary>
    private void AppendExists(StringBuilder sql, List<Match> joins)
    {
        if (joins.Count == 0)
        {
            AppendWhere(sql, []);
            return;
        }

        sql.Append(" WHERE EXISTS (SELECT 1 FROM ").Append(string.Join(", ", joins.Select(m => $"\"{m.Groups[2].Value}\" {m.Groups[3].Value}")));
        var conditions = joins.Select(m => m.Groups[4].Value).Concat(_predicates).ToList();
        for (var i = 0; i < conditions.Count; i++)
            sql.Append(i == 0 ? " WHERE (" : " AND (").Append(conditions[i]).Append(')');
        sql.Append(')');
    }

    private void Update(StringBuilder sql, SqlDialect dialect)
    {
        var joins = _joins.Select(j => JoinShape.Match(j)).ToList();
        if (dialect == SqlDialect.Oracle)
        {
            // No UPDATE … FROM / JOIN: the navigations of the predicate in an EXISTS (a SET reading a navigation is not expressible this way: the database refuses the alias); no RETURNING rows (the plan has no Oracle text then).
            sql.Append("UPDATE \"").Append(_entity.Table).Append("\" t SET ").Append(string.Join(", ", _sets));
            AppendExists(sql, joins);
            return;
        }

        if (dialect == SqlDialect.MySql)
        {
            // MySQL's multi-table UPDATE: the joins before SET, the columns set under the alias (a joined table may share their names); no RETURNING (the text keeps it, so the call fails there, as PRAG3012 says).
            sql.Append("UPDATE \"").Append(_entity.Table).Append("\" t");
            foreach (var join in _joins)
                sql.Append(' ').Append(Inner(join));
            sql.Append(" SET ").Append(string.Join(", ", _sets.Select(s => "t." + s)));
            AppendWhere(sql, []);
            if (_returning is not null)
                sql.Append(" RETURNING ").Append(string.Join(", ", ReturningColumns()));
            return;
        }

        if (dialect == SqlDialect.SqlServer)
        {
            sql.Append("UPDATE t SET ").Append(string.Join(", ", _sets));
            if (_returning is not null)
                sql.Append(" OUTPUT ").Append(string.Join(", ", ReturningColumns().Select(c => c.Replace("t.\"", "INSERTED.\""))));
            sql.Append(" FROM \"").Append(_entity.Table).Append("\" t");
            foreach (var join in _joins)
                sql.Append(' ').Append(Inner(join));
            AppendWhere(sql, []);
            return;
        }

        sql.Append("UPDATE \"").Append(_entity.Table).Append("\" AS t SET ").Append(string.Join(", ", _sets));
        if (joins.Count > 0)
            sql.Append(" FROM ").Append(string.Join(", ", joins.Select(m => $"\"{m.Groups[2].Value}\" {m.Groups[3].Value}"))); // the joined tables; their conditions in the WHERE
        AppendWhere(sql, joins.Select(m => m.Groups[4].Value));
        if (_returning is not null)
            sql.Append(" RETURNING ").Append(string.Join(", ", ReturningColumns().Select(c => dialect == SqlDialect.Sqlite ? c.Replace("t.\"", "\"") : c)));
    }

    private void Delete(StringBuilder sql, SqlDialect dialect)
    {
        var joins = _joins.Select(j => JoinShape.Match(j)).ToList();
        switch (dialect)
        {
            case SqlDialect.SqlServer:
                sql.Append("DELETE t");
                if (_returning is not null)
                    sql.Append(" OUTPUT ").Append(string.Join(", ", ReturningColumns().Select(c => c.Replace("t.\"", "DELETED.\""))));
                sql.Append(" FROM \"").Append(_entity.Table).Append("\" t");
                foreach (var join in _joins)
                    sql.Append(' ').Append(Inner(join));
                AppendWhere(sql, []);
                return;
            case SqlDialect.MySql:
                // The multi-table form, with or without joins; no RETURNING (kept in the text so the call fails there, as PRAG3012 says).
                sql.Append("DELETE t FROM \"").Append(_entity.Table).Append("\" t");
                foreach (var join in _joins)
                    sql.Append(' ').Append(Inner(join));
                AppendWhere(sql, []);
                break;
            case SqlDialect.PostgreSql:
                sql.Append("DELETE FROM \"").Append(_entity.Table).Append("\" AS t");
                if (joins.Count > 0)
                    sql.Append(" USING ").Append(string.Join(", ", joins.Select(m => $"\"{m.Groups[2].Value}\" {m.Groups[3].Value}")));
                AppendWhere(sql, joins.Select(m => m.Groups[4].Value));
                break;
            case SqlDialect.Oracle:
                // No AS before a table alias, no USING: the joined rows in an EXISTS, as on SQLite.
                sql.Append("DELETE FROM \"").Append(_entity.Table).Append("\" t");
                AppendExists(sql, joins);
                return;
            default:
                // SQLite: no USING; the joined rows and the predicates inside an EXISTS over the row.
                sql.Append("DELETE FROM \"").Append(_entity.Table).Append("\" AS t");
                if (joins.Count > 0)
                {
                    sql.Append(" WHERE EXISTS (SELECT 1 FROM ").Append(string.Join(", ", joins.Select(m => $"\"{m.Groups[2].Value}\" {m.Groups[3].Value}")));
                    var conditions = joins.Select(m => m.Groups[4].Value).Concat(_predicates).ToList();
                    for (var i = 0; i < conditions.Count; i++)
                        sql.Append(i == 0 ? " WHERE (" : " AND (").Append(conditions[i]).Append(')');
                    sql.Append(')');
                }
                else
                    AppendWhere(sql, []);
                break;
        }

        if (_returning is not null)
            sql.Append(" RETURNING ").Append(string.Join(", ", ReturningColumns().Select(c => dialect == SqlDialect.Sqlite ? c.Replace("t.\"", "\"") : c)));
    }

    private IEnumerable<string> ReturningColumns() => _returning!.Columns;

    /// <summary>
    ///     The MERGE of the set the chain names with the rows given at the call: matched rows updated in their other columns, new ones inserted,
    ///     set rows not among the given deleted. SQLite has no MERGE: an INSERT … ON CONFLICT DO UPDATE and a DELETE of the keys not given.
    /// </summary>
    private void Sync(StringBuilder sql, SqlDialect dialect)
    {
        var columns = _entity.Columns.Where(c => !c.IsDiscriminator).ToList();
        var key = _entity.Keys[0];
        var names = string.Join(", ", columns.Select(c => $"\"{c.Column}\""));
        var sets = string.Join(", ", columns.Where(c => c != key).Select(c => $"\"{c.Column}\" = {(dialect == SqlDialect.Sqlite ? "excluded" : "s")}.\"{c.Column}\""));
        var predicates = _predicates.Count == 0 ? "" : " AND " + string.Join(" AND ", _predicates.Select(p => "(" + p + ")"));
        switch (dialect)
        {
            case SqlDialect.Sqlite:
                sql.Append("INSERT INTO \"").Append(_entity.Table).Append("\" (").Append(names).Append(") VALUES {rows} ON CONFLICT (\"").Append(key.Column).Append("\") DO UPDATE SET ").Append(sets);
                sql.Append("; DELETE FROM \"").Append(_entity.Table).Append("\" AS t WHERE t.\"").Append(key.Column).Append("\" NOT IN ({keys})").Append(predicates);
                break;
            case SqlDialect.MySql:
                // No MERGE: the upsert on the key (ON DUPLICATE KEY, the proposed row aliased s) and the delete of the keys not given, as on SQLite.
                sql.Append("INSERT INTO \"").Append(_entity.Table).Append("\" (").Append(names).Append(") VALUES {rows} AS s ON DUPLICATE KEY UPDATE ").Append(sets);
                sql.Append("; DELETE FROM \"").Append(_entity.Table).Append("\" AS t WHERE t.\"").Append(key.Column).Append("\" NOT IN ({keys})").Append(predicates);
                break;
            case SqlDialect.Oracle:
                // MERGE without NOT MATCHED BY SOURCE: the upsert from the rows (23ai's VALUES constructor) and the delete of the keys not given, in one block.
                sql.Append("MERGE INTO \"").Append(_entity.Table).Append("\" t USING (SELECT * FROM (VALUES {rows}) v (").Append(names).Append(")) s ON (t.\"").Append(key.Column).Append("\" = s.\"").Append(key.Column).Append("\")");
                sql.Append(" WHEN MATCHED THEN UPDATE SET ").Append(sets);
                sql.Append(" WHEN NOT MATCHED THEN INSERT (").Append(names).Append(") VALUES (").Append(string.Join(", ", columns.Select(c => $"s.\"{c.Column}\""))).Append(')');
                sql.Append("; DELETE FROM \"").Append(_entity.Table).Append("\" t WHERE t.\"").Append(key.Column).Append("\" NOT IN ({keys})").Append(predicates);
                break;
            case SqlDialect.PostgreSql:
                sql.Append("MERGE INTO \"").Append(_entity.Table).Append("\" AS t USING (VALUES {rows}) AS s (").Append(names).Append(") ON t.\"").Append(key.Column).Append("\" = s.\"").Append(key.Column).Append('"');
                sql.Append(" WHEN MATCHED THEN UPDATE SET ").Append(sets);
                sql.Append(" WHEN NOT MATCHED THEN INSERT (").Append(names).Append(") VALUES (").Append(string.Join(", ", columns.Select(c => $"s.\"{c.Column}\""))).Append(')');
                sql.Append(" WHEN NOT MATCHED BY SOURCE").Append(predicates).Append(" THEN DELETE");
                break;
            default:
                sql.Append("MERGE \"").Append(_entity.Table).Append("\" WITH (HOLDLOCK) AS t USING (VALUES {rows}) AS s (").Append(names).Append(") ON t.\"").Append(key.Column).Append("\" = s.\"").Append(key.Column).Append('"');
                sql.Append(" WHEN MATCHED THEN UPDATE SET ").Append(sets);
                sql.Append(" WHEN NOT MATCHED BY TARGET THEN INSERT (").Append(names).Append(") VALUES (").Append(string.Join(", ", columns.Select(c => $"s.\"{c.Column}\""))).Append(')');
                sql.Append(" WHEN NOT MATCHED BY SOURCE").Append(predicates).Append(" THEN DELETE;");
                break;
        }
    }
}
