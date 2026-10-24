using System.Data.Common;
using System.Text.RegularExpressions;
using Pragmatic.Result;
using Pragmatic.Result.Database;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The rule a failed save broke, as <c>EfCoreUnitOfWork</c> tells it (<c>RuleViolationClassifier</c> over <c>ClassifyRuleViolation</c>): a
///     duplicate is a conflict naming the fields its constraint covers, a delete a relation refuses is "in use" naming what uses the row, any
///     other broken rule a constraint error. What is not a broken rule (a deadlock, a lost connection) is not classified: it stays the provider's
///     exception, for a retry to see.
/// </summary>
/// <remarks>
///     INVESTIGATE: the kind is read from the SQLSTATE where the provider gives it (PostgreSQL) and from the provider's message elsewhere, as
///     Pragmatic's <c>HeuristicDbExceptionParser</c> does: this assembly references no driver, so SQL Server's and Oracle's error numbers are
///     not at hand, and a server speaking another language than English words its messages otherwise. The driver helper packages
///     (<c>Pragmatic.Persistence.Sql.SqlClient</c> and the others) could read the numbers, as EF Core's provider parsers do.
/// </remarks>
internal static partial class SqlRuleViolations
{
    private enum Kind
    {
        Unique,
        ForeignKey,
        Other,
    }

    /// <summary>The broken rule as Pragmatic's error; null when the failure is not a broken rule.</summary>
    public static IError? Classify(DbException exception, IReadOnlyList<SqlEntry> written)
    {
        var message = exception.Message;
        if (KindOf(exception.SqlState, message) is not { } kind)
            return null;
        var constraint = ConstraintName(message);
        var map = MapOf(message, constraint, written);
        var entityType = map?.Type.Name ?? written[0].Entity.GetType().Name;
        switch (kind)
        {
            case Kind.Unique:
                return DbConflictError.UniqueViolation(entityType, map is null ? null : Fields(message, constraint, map), constraint);
            case Kind.ForeignKey:
                var deleted = written.Where(e => e.State == SqlEntryState.Deleted).Select(e => e.Entity.GetType()).Distinct().ToList();
                if (deleted.Count > 0 && InUse(deleted, constraint) is { } inUse)
                    return inUse;
                return new DbConstraintError { ConstraintType = "ForeignKey", ConstraintName = constraint, TableName = map?.Table ?? entityType, Details = "Referenced entity does not exist or cannot be deleted due to existing references." };
            default:
                return new DbConstraintError { Details = message };
        }
    }

    private static Kind? KindOf(string? sqlState, string message)
    {
        switch (sqlState)
        {
            case "23505":
                return Kind.Unique;
            case "23503":
                return Kind.ForeignKey;
            case "23502" or "23514":
                return Kind.Other; // a null, a check
        }

        bool Has(string phrase) => message.Contains(phrase, StringComparison.OrdinalIgnoreCase);
        if (Has("UNIQUE constraint failed") || Has("Duplicate entry") || Has("duplicate key") || Has("ORA-00001"))
            return Kind.Unique;
        if (Has("FOREIGN KEY constraint") || Has("REFERENCE constraint") || Has("foreign key constraint fails") || Has("ORA-02291") || Has("ORA-02292"))
            return Kind.ForeignKey;
        if (Has("NOT NULL constraint failed") || Has("cannot be null") || Has("Cannot insert the value NULL") || Has("ORA-01400") || Has("CHECK constraint"))
            return Kind.Other;
        return null;
    }

    /// <summary>
    ///     The constraint the provider named: <c>"IX_…"</c> (PostgreSQL, SQL Server, MySQL under ANSI_QUOTES), <c>'IX_…'</c>, <c>key 'Table.IX_…'</c>
    ///     (MySQL's duplicate), Oracle's <c>(SCHEMA.IX_…)</c> after its code: Oracle words its messages in the client's language (Italian on this
    ///     machine, measured), so only the code and the parentheses are read there.
    /// </summary>
    private static string? ConstraintName(string message)
    {
        var match = Named().Match(message);
        return match.Success ? match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value : null;
    }

    [GeneratedRegex("""ORA-0(?:0001|2291|2292):[^(]*\((?:[^().]+\.)?([^()."]+)\)|constraint "([^"]+)"|index '([^']+)'|CONSTRAINT `([^`]+)`|for key '(?:[^'.]+\.)?([^']+)'|constraint '([^']+)'""", RegexOptions.IgnoreCase)]
    private static partial Regex Named();

    /// <summary>The written entity whose table the message or the constraint names (the longest name first: Reviews before Review).</summary>
    private static SqlEntityMap? MapOf(string message, string? constraint, IReadOnlyList<SqlEntry> written)
        => written.Select(e => SqlEntityMap.For(e.Entity.GetType())).OfType<SqlEntityMap>().Distinct().OrderByDescending(m => m.Table.Length)
            .FirstOrDefault(m => message.Contains(m.Table, StringComparison.Ordinal) || constraint?.Contains(m.Table, StringComparison.Ordinal) == true);

    /// <summary>
    ///     The properties a duplicate is about: SQLite lists the columns (<c>Table.Column, …</c>); elsewhere the constraint's name holds them
    ///     (Pragmatic.Migrations' <c>IX_{Table}_{Columns}</c>), every one in the order the constraint names them, joined.
    /// </summary>
    private static string? Fields(string message, string? constraint, SqlEntityMap map)
    {
        var tokens = message.IndexOf("constraint failed:", StringComparison.OrdinalIgnoreCase) is var at and >= 0
            ? message[(at + "constraint failed:".Length)..].Split(',', '\'').Select(p => p.Trim()).Select(p => p.Contains('.') ? p[(p.IndexOf('.') + 1)..] : p)
            : constraint?.Split('_') ?? [];
        var fields = tokens.Select(t => map.Columns.FirstOrDefault(c => c.Column == t)?.Property).OfType<string>().Distinct().ToList();
        return fields.Count == 0 ? null : string.Join(", ", fields);
    }

    /// <summary>
    ///     A delete a relation refuses: the deleted type and what uses it, found by the constraint's name (the dependent's table in it), or as the
    ///     one relation into the deleted type when the provider names none (SQLite); with several and no name, what uses it is not guessed.
    ///     A named constraint of no relation into a deleted type is another write's violation (an insert naming a missing row): null.
    /// </summary>
    private static DbInUseError? InUse(List<Type> deleted, string? constraint)
    {
        var relations = SqlEntityMap.All
            .SelectMany(m => m.Navigations.Where(n => deleted.Contains(n.Target)).Select(n => (Dependent: m, Principal: n.Target)))
            .ToList();
        var relation = constraint is null
            ? relations.Count == 1 ? relations[0] : default
            : relations.Where(r => constraint.Contains(r.Dependent.Table, StringComparison.Ordinal)).DefaultIfEmpty().MaxBy(r => r.Dependent?.Table.Length ?? 0);
        if (relation.Dependent is null && constraint is not null)
            return null;
        return new DbInUseError { EntityType = (relation.Principal ?? deleted[0]).Name, UsedBy = relation.Dependent?.Type.Name };
    }
}
