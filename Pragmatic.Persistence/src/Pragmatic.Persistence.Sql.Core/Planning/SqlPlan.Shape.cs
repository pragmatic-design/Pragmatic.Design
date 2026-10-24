using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     Client shaping over a compiled plan: the plan's statement becomes a derived table whose columns are the row type's members
///     (the generator aliases the select list of a <c>ShapeAsync</c> plan), and the client's filter, sort, page and selection are
///     written over it, each field checked against <see cref="ShapeColumns" />. The compiled chain keeps what the server decided
///     (the rows the client may see, the joins, the visibility rules); the client decides how to look at them. One command: the page,
///     and the count of the filtered rows when asked.
/// </summary>
public sealed partial class SqlPlan<T>
{
    /// <summary>The members a <c>ShapeAsync</c> plan exposes, in the select list's order; null on the other plans.</summary>
    public SqlShapeColumn[]? ShapeColumns { get; }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<SqlShapeResult<T>> ShapeAsync(SqlQuery<T> query, SqlShape shape, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var columns = ShapeColumns ?? throw new InvalidOperationException("This plan was not compiled for ShapeAsync: end the chain in ShapeAsync at the call site.");
        if (Load is not null)
            throw new NotSupportedException("A shaped read works on one statement: a collection Include has companions, project the rows instead.");
        // INVESTIGATE: the shape could wrap the caller's variant, its filters' values after its own parameters; refused until it does.
        if (Dynamic)
            throw new NotSupportedException("A shaped read over Pragmatic's filtered entities is not supported yet: the statement depends on the caller's filters.");
        var session = query.Session;
        var dialect = session.Dialect;
        var kinds = new List<SqlParamKind>(Parameters);
        var values = new List<object?>();
        var sql = new StringBuilder();

        // The selection: every member, or the client's subset (each a whitelisted member).
        var selected = shape.Select is null ? columns : shape.Select.Select(name => Column(columns, name)).ToArray();
        sql.Append("SELECT ").Append(string.Join(", ", selected.Select(c => "s." + SqlDialects.Quote(dialect, c.Name))));
        var from = new StringBuilder(" FROM (").Append(Sql(dialect)).Append(") s");
        var where = new StringBuilder();
        if (shape.Filter is not null)
            where.Append(" WHERE ").Append(Filter(shape.Filter, columns, shape.CaseInsensitive, dialect, kinds, values));
        sql.Append(from).Append(where);
        if (shape.Sort.Count > 0)
            sql.Append(" ORDER BY ").Append(string.Join(", ", shape.Sort.Select(s => "s." + SqlDialects.Quote(dialect, Column(columns, s.Field).Name) + (s.Descending ? " DESC" : ""))));
        else if ((shape.Skip > 0 || shape.Take is not null) && dialect == SqlDialect.SqlServer)
            sql.Append(" ORDER BY (SELECT NULL)");
        string? skipParameter = null, takeParameter = null;
        if (shape.Skip > 0)
        {
            skipParameter = Add(kinds, values, SqlParamKind.Int32, shape.Skip);
        }

        if (shape.Take is { } take)
            takeParameter = Add(kinds, values, SqlParamKind.Int32, take);
        sql.Append(SqlDialects.Paging(dialect, skipParameter, takeParameter));
        if (shape.RequireTotalCount)
            sql.Append("; SELECT COUNT(*)").Append(from).Append(where);

        using var command = session.FreshCommand(sql.ToString(), kinds.ToArray());
        Bind(query, command.Parameters, dialect); // the plan's own parameters first
        for (var i = 0; i < values.Count; i++)
            command.Parameters[Parameters.Length + i].Value = SqlValue.ToDatabase(dialect, values[i]);
        var execution = SqlTelemetry.Begin(session, Origin, command);
        Exception? failure = null;
        try
        {
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var rows = new List<T>();
            List<Dictionary<string, object?>>? narrowed = shape.Select is null ? null : [];
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (narrowed is null)
                {
                    rows.Add(Read!(reader, dialect));
                    continue;
                }

                var row = new Dictionary<string, object?>(selected.Length, StringComparer.Ordinal);
                for (var i = 0; i < selected.Length; i++)
                    row[selected[i].Name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                narrowed.Add(row);
            }

            int? total = null;
            if (shape.RequireTotalCount && await reader.NextResultAsync(ct).ConfigureAwait(false) && await reader.ReadAsync(ct).ConfigureAwait(false))
                total = SqlValue.Count(reader.GetValue(0));
            return new SqlShapeResult<T>(rows, narrowed, total, selected.Select(c => c.Name).ToArray());
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            SqlTelemetry.End(execution, session, Origin, failure);
        }
    }

    private static SqlShapeColumn Column(SqlShapeColumn[] columns, string name)
    {
        foreach (var column in columns)
        {
            if (string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase))
                return column;
        }

        throw new SqlShapeException($"'{name}' is not a member of the rows: the chain exposes {string.Join(", ", columns.Select(c => c.Name))}.");
    }

    private static string Add(List<SqlParamKind> kinds, List<object?> values, SqlParamKind kind, object? value)
    {
        kinds.Add(kind);
        values.Add(value);
        return "@p" + (kinds.Count - 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The filter as SQL over the derived table <c>s</c>, its values as parameters converted to the columns' kinds.</summary>
    private static string Filter(SqlShapeFilter filter, SqlShapeColumn[] columns, bool caseInsensitive, SqlDialect dialect, List<SqlParamKind> kinds, List<object?> values)
    {
        switch (filter)
        {
            case SqlShapeFilter.And and:
                return "(" + string.Join(" AND ", and.Terms.Select(t => Filter(t, columns, caseInsensitive, dialect, kinds, values))) + ")";
            case SqlShapeFilter.Or or:
                return "(" + string.Join(" OR ", or.Terms.Select(t => Filter(t, columns, caseInsensitive, dialect, kinds, values))) + ")";
            case SqlShapeFilter.Not not:
                return "NOT " + Filter(not.Inner, columns, caseInsensitive, dialect, kinds, values);
            case SqlShapeFilter.Comparison comparison:
            {
                var column = Column(columns, comparison.Field);
                var name = "s." + SqlDialects.Quote(dialect, column.Name);
                var isText = column.Kind == SqlParamKind.String;
                var lower = isText && caseInsensitive;
                var left = lower ? $"LOWER({name})" : name;
                if (comparison.Value is null && comparison.Operator is SqlShapeOperator.Equal or SqlShapeOperator.NotEqual)
                    return comparison.Operator == SqlShapeOperator.Equal ? $"{name} IS NULL" : $"{name} IS NOT NULL";
                var value = SqlShapeFilter.Convert(comparison.Value, column.Kind, comparison.Field);
                switch (comparison.Operator)
                {
                    case SqlShapeOperator.Contains or SqlShapeOperator.NotContains or SqlShapeOperator.StartsWith or SqlShapeOperator.EndsWith:
                    {
                        if (!isText)
                            throw new SqlShapeException($"'{comparison.Field}' is not text: {comparison.Operator} applies to text members.");
                        var text = (string)value!;
                        var pattern = comparison.Operator switch
                        {
                            SqlShapeOperator.StartsWith => Escape(text) + "%",
                            SqlShapeOperator.EndsWith => "%" + Escape(text),
                            _ => "%" + Escape(text) + "%",
                        };
                        var parameter = Add(kinds, values, SqlParamKind.String, pattern);
                        var right = lower ? $"LOWER({parameter})" : parameter;
                        var like = $"{left} LIKE {right} ESCAPE '{(dialect == SqlDialect.MySql ? "\\\\" : "\\")}'"; // MySQL reads a backslash in a literal as an escape: doubled
                        return comparison.Operator == SqlShapeOperator.NotContains ? $"NOT ({like})" : like;
                    }

                    default:
                    {
                        var parameter = Add(kinds, values, column.Kind, value);
                        var right = lower ? $"LOWER({parameter})" : parameter;
                        var op = comparison.Operator switch
                        {
                            SqlShapeOperator.Equal => "=",
                            SqlShapeOperator.NotEqual => "<>",
                            SqlShapeOperator.Greater => ">",
                            SqlShapeOperator.GreaterOrEqual => ">=",
                            SqlShapeOperator.Less => "<",
                            _ => "<=",
                        };
                        return $"{left} {op} {right}";
                    }
                }
            }

            default:
                throw new SqlShapeException("Unknown filter node.");
        }
    }

    /// <summary>The LIKE wildcards of a client's text escaped, so "100%" means the characters.</summary>
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
