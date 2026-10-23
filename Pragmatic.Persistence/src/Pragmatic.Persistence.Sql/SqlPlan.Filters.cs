using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     A plan whose statement depends on the execution: it filters Pragmatic's entities (<see cref="SqlPlanFilters" />: a variant per
///     combination of filter states, built once), or it reads a predicate composed at runtime (<see cref="SqlPredicate{T}" />, rendered
///     where the plan marks it, <c>{dyn:k}</c> for slot <c>k</c>). Parameters: the plan's own, then the filter values
///     (<see cref="SqlPlanFilters.ValueCount" /> of them, those the variant names), then the predicates' values.
/// </summary>
public sealed partial class SqlPlan<T>
{
    private readonly SqlPlanFilters? _filters;
    private readonly int[]? _predicates;
    private ConcurrentDictionary<(SqlDialect Dialect, string? Schema, ulong Mask), Variant>? _variants;

    /// <summary>One combination's statement, and which of the value parameters (tenant, user, scopes, instant) it names.</summary>
    private sealed class Variant(string sql, bool[] values)
    {
        public string Sql { get; } = sql;

        public bool[] Values { get; } = values;
    }

    /// <summary>True when the statement depends on the execution: the plan filters Pragmatic's entities, or reads a predicate composed at runtime.</summary>
    public bool Filtered => Dynamic;

    /// <summary>True when the statement is chosen or assembled per execution (filters, predicates composed at runtime).</summary>
    private bool Dynamic => _filters is not null || _predicates is not null;

    /// <summary>The index of the first predicate value: after the plan's own parameters and the filter values.</summary>
    private int FirstPredicateParameter => Parameters.Length + SqlPlanFilters.ValueCount;

    private Variant VariantFor(SqlSession session, ulong mask)
    {
        var variants = _variants ??= new ConcurrentDictionary<(SqlDialect, string?, ulong), Variant>();
        return variants.GetOrAdd((session.Dialect, session.Schema, mask), static (key, state) =>
        {
            var (plan, session) = state;
            var sql = SqlPlanFilters.Apply(plan.Qualified(session), key.Mask);
            var values = new bool[SqlPlanFilters.ValueCount];
            for (var i = 0; plan._filters is not null && i < values.Length; i++)
                values[i] = Names(sql, "@p" + (plan._filters.Values + i).ToString(CultureInfo.InvariantCulture));
            return new Variant(sql, values);
        }, (this, session));
    }

    /// <summary>True when <paramref name="sql" /> names the parameter (not a longer one that starts the same: <c>@p1</c> in <c>@p12</c>).</summary>
    private static bool Names(string sql, string parameter)
    {
        for (var at = sql.IndexOf(parameter, StringComparison.Ordinal); at >= 0; at = sql.IndexOf(parameter, at + 1, StringComparison.Ordinal))
        {
            var next = at + parameter.Length;
            if (next == sql.Length || !char.IsDigit(sql[next]) && sql[next] != '_')
                return true;
        }

        return false;
    }

    /// <summary>The variant's text with the chain's predicates rendered in their marks; their values, in parameter order (null without predicates).</summary>
    private string WithPredicates(string sql, in SqlQuery<T> query, out List<(SqlParamKind Kind, object? Value)>? values)
    {
        values = null;
        if (_predicates is null)
            return sql;
        values = [];
        foreach (var slot in _predicates)
        {
            if (query.Slot(slot) is not SqlComposed composed)
                throw new InvalidOperationException($"Plan {Origin} expects a predicate or an ordering in slot {slot}.");
            var text = new StringBuilder();
            composed.Render(query.Session.Dialect, "t", text, values, FirstPredicateParameter);
            sql = sql.Replace("{dyn:" + slot.ToString(CultureInfo.InvariantCulture) + "}", text.ToString(), StringComparison.Ordinal);
        }

        return sql;
    }

    /// <summary>The command of the execution's statement in the session, values bound; prepared once per statement, per call where an IN list expands.</summary>
    private DbCommand DynamicCommand(in SqlQuery<T> query)
    {
        var session = query.Session;
        if (Expands(session.Dialect))
            return DynamicToCommand(query);
        var mask = _filters?.Mask(session) ?? 0;
        var variant = VariantFor(session, mask);
        var sql = WithPredicates(variant.Sql, query, out var values);
        var command = session.CommandFor(Id, mask, values is null ? null : sql);
        if (command is null)
        {
            command = Create(session, sql, query);
            AddValueParameters(command, variant, values, session);
            // A PostgreSQL array types itself from its value, an untyped predicate value is bound as it comes: neither is prepared ahead.
            var typed = values is null || values.TrueForAll(v => v.Kind != SqlParamKind.Unknown);
            if (Preparable && typed && !(variant.Values[2] && session.Dialect == SqlDialect.PostgreSql) && session.Prepares(sql))
                command.Prepare();
            session.Keep(Id, mask, values is null ? null : sql, command);
        }

        command.Transaction = session.Transaction;
        Bind(query, command.Parameters, session.Dialect);
        BindValues(command, variant, values, session);
        return command;
    }

    /// <summary><see cref="ToCommand" /> for a dynamic plan: a fresh command of the execution's statement.</summary>
    private DbCommand DynamicToCommand(in SqlQuery<T> query)
    {
        var session = query.Session;
        var variant = VariantFor(session, _filters?.Mask(session) ?? 0);
        var sql = WithPredicates(variant.Sql, query, out var values);
        if (!Expands(session.Dialect))
        {
            var plain = Create(session, sql, query);
            AddValueParameters(plain, variant, values, session);
            Bind(query, plain.Parameters, session.Dialect);
            BindValues(plain, variant, values, session);
            return plain;
        }

        var probe = Probe(query);
        var command = Create(session, ExpandLists(sql, probe), query);
        AddValueParameters(command, variant, values, session);
        BindValues(command, variant, values, session); // before the lists expand: their items go after, and the lists' own parameters leave
        ExpandListValues(command, probe, session.Dialect);
        if (session.Dialect == SqlDialect.Oracle)
            SqlOracle.Adapt(command);
        return command;
    }

    /// <summary>
    ///     The parameters after the plan's own: the filter values the variant names (the tenant and the user as text, the scopes as an array
    ///     column), then the predicates' values.
    /// </summary>
    private void AddValueParameters(DbCommand command, Variant variant, List<(SqlParamKind Kind, object? Value)>? values, SqlSession session)
    {
        for (var i = 0; i < variant.Values.Length; i++)
        {
            if (!variant.Values[i])
                continue;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + (_filters!.Values + i).ToString(CultureInfo.InvariantCulture);
            SqlValue.Configure(parameter, i switch { 2 => SqlParamKind.Array, 3 => SqlParamKind.DateTimeOffset, _ => SqlParamKind.String }, session.Dialect);
            command.Parameters.Add(parameter);
        }

        for (var i = 0; values is not null && i < values.Count; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + (FirstPredicateParameter + i).ToString(CultureInfo.InvariantCulture);
            SqlValue.Configure(parameter, values[i].Kind, session.Dialect);
            command.Parameters.Add(parameter);
        }

        if (session.Dialect == SqlDialect.Oracle)
            SqlOracle.Adapt(command); // the names added after the plan's (Create adapted those)
    }

    /// <summary>The caller's tenant, user, scopes and instant, then the predicates' values, on the parameters after the plan's: a missing value is NULL, which no row matches.</summary>
    private void BindValues(DbCommand command, Variant variant, List<(SqlParamKind Kind, object? Value)>? values, SqlSession session)
    {
        var filters = session.Filters;
        var index = Parameters.Length;
        for (var i = 0; i < variant.Values.Length; i++)
        {
            if (!variant.Values[i])
                continue;
            object? value = i switch
            {
                0 => filters?.TenantId,
                1 => filters?.UserId,
                2 => SqlArrays.ToStore(session.Dialect, filters?.Scopes ?? (IEnumerable<string>)[]),
                _ => filters is null ? null : SqlValue.ToDatabase(session.Dialect, filters.Now),
            };
            command.Parameters[index++].Value = value ?? DBNull.Value;
        }

        for (var i = 0; values is not null && i < values.Count; i++)
            command.Parameters[index++].Value = SqlValue.ToDatabase(session.Dialect, values[i].Value);
    }
}
