using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>The SQL that matches a collection's children to their parents on every key column: the runtime path's form of the generator's.</summary>
internal static class SqlKeyMatch
{
    /// <summary>A composite parent's collection is matched on one foreign key (or join-table column) per key column; one with a single column cannot be.</summary>
    public static void Check(SqlEntityMap owner, SqlCollection collection)
    {
        if (owner.HasCompositeKey && collection.ForeignKeyColumns.Length != owner.KeyColumns.Length)
            throw new NotSupportedException($"{owner.Type.Name} has a composite key ({string.Join(", ", owner.KeyColumns)}): {(collection.IsLink ? $"the join table \"{collection.LinkTable}\" holds one column for it ([ManyToMany] names one per key column, comma-separated)" : $"{collection.Child.Name} carries no foreign key per key column")}.");
    }

    /// <summary>The parent statement's select list of its keys: the key column, or the key columns as k0, k1, … for a composite key.</summary>
    public static string ParentKeys(SqlEntityMap owner, string alias, bool composite)
    {
        if (!composite)
            return $"{alias}.\"{owner.KeyColumn}\"";
        var list = new StringBuilder();
        for (var i = 0; i < owner.KeyColumns.Length; i++)
            list.Append(i > 0 ? ", " : "").Append(alias).Append(".\"").Append(owner.KeyColumns[i]).Append("\" AS k").Append(i);
        return list.ToString();
    }

    /// <summary>The rows under <paramref name="side" /> whose foreign keys are among the parent statement's keys: <c>IN</c>, or <c>EXISTS</c> matched column by column (SQL Server has no row-value IN).</summary>
    public static string ParentRows(SqlDialect dialect, string side, string[] foreignKeys, string parentStatement)
    {
        if (foreignKeys.Length == 1)
            return $"{side}.\"{foreignKeys[0]}\" IN ({SqlDialects.KeySubquery(dialect, parentStatement)})";
        var match = new StringBuilder();
        for (var i = 0; i < foreignKeys.Length; i++)
            match.Append(i > 0 ? " AND " : "").Append("pk.k").Append(i).Append(" = ").Append(side).Append(".\"").Append(foreignKeys[i]).Append('"');
        return $"EXISTS (SELECT 1 FROM ({parentStatement}) pk WHERE {match})";
    }

    /// <summary>The child rows under <paramref name="side" /> of the parent row under <paramref name="ownerAlias" />: one equality per key column.</summary>
    public static string Matches(string[] foreignKeys, string side, SqlEntityMap owner, string ownerAlias)
    {
        var match = new StringBuilder();
        for (var i = 0; i < foreignKeys.Length; i++)
            match.Append(i > 0 ? " AND " : "").Append(side).Append(".\"").Append(foreignKeys[i]).Append("\" = ").Append(ownerAlias).Append(".\"").Append(foreignKeys.Length > 1 ? owner.KeyColumns[i] : owner.KeyColumn).Append('"');
        return match.ToString();
    }

    /// <summary>The columns under <paramref name="alias" />, comma-separated: a window's partition, an ordering.</summary>
    public static string List(string[] columns, string alias) => string.Join(", ", columns.Select(c => $"{alias}.\"{c}\""));
}
