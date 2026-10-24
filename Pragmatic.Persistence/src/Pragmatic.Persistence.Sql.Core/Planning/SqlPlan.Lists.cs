using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     IN lists in a plan: on SQLite and SQL Server the text names one parameter per item (<c>@p3_0, @p3_1, …</c>, NULL for
///     an empty list, which matches nothing) and the command is built per call; PostgreSQL binds the list as one array parameter
///     (<c>= ANY(@p3)</c>) and keeps the prepared command.
/// </summary>
public sealed partial class SqlPlan<T>
{
    private static readonly Regex ListPlaceholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    /// <summary>True where a list parameter expands the statement per call: SQLite and SQL Server; PostgreSQL binds the array.</summary>
    private bool Expands(SqlDialect dialect) => HasLists && dialect != SqlDialect.PostgreSql;

    /// <summary>The query's values bound into parameters that only hold them (a provider may refuse a list as a parameter's value).</summary>
    private SqlProbeParameters Probe(in SqlQuery<T> query)
    {
        var probe = new SqlProbeParameters(Parameters.Length);
        Bind(query, probe, query.Session.Dialect);
        return probe;
    }

    private string ExpandLists(string sql, SqlProbeParameters probe)
    {
        for (var i = 0; i < Parameters.Length; i++)
        {
            if (Parameters[i] != SqlParamKind.List)
                continue;
            var name = "@p" + i.ToString(CultureInfo.InvariantCulture);
            var text = new StringBuilder();
            var n = 0;
            if (probe[i].Value is IEnumerable values and not string)
            {
                foreach (var _ in values)
                    text.Append(n > 0 ? ", " : "").Append(name).Append('_').Append(n++.ToString(CultureInfo.InvariantCulture));
            }

            sql = sql.Replace("{" + i.ToString(CultureInfo.InvariantCulture) + "}", n == 0 ? "NULL" : text.ToString());
        }

        return sql;
    }

    /// <summary>The probe's values on the command: each scalar on its parameter, each list's items on parameters of their own (<c>@p3_0, @p3_1, …</c>) in the list's place.</summary>
    private void ExpandListValues(DbCommand command, SqlProbeParameters probe, SqlDialect dialect)
    {
        for (var i = 0; i < Parameters.Length; i++)
        {
            if (Parameters[i] != SqlParamKind.List)
                command.Parameters[i].Value = probe[i].Value;
        }

        for (var i = Parameters.Length - 1; i >= 0; i--)
        {
            if (Parameters[i] != SqlParamKind.List)
                continue;
            var list = command.Parameters[i];
            var values = probe[i].Value as IEnumerable;
            command.Parameters.RemoveAt(i);
            if (values is null || values is string)
                continue;
            var n = 0;
            foreach (var item in values)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = list.ParameterName + "_" + n++.ToString(CultureInfo.InvariantCulture);
                parameter.Value = SqlValue.ToDatabase(dialect, item);
                command.Parameters.Add(parameter);
            }
        }
    }
}
