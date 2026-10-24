using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     The columns the database fills (<c>[DatabaseGenerated]</c>): left out of the writer's columns, so no insert, update or upsert writes
///     them, and read back by key into the object (after an insert; after an update too when one of them changes on updates).
/// </summary>
public sealed class SqlGenerated<T>(string[] columns, bool afterUpdate, Action<T, DbDataReader, SqlDialect> read)
{
    public string[] Columns { get; } = columns;

    /// <summary>True when a column changes on updates (<c>OnUpdate</c>): an update reads them back too.</summary>
    public bool AfterUpdate { get; } = afterUpdate;

    /// <summary>Assigns the row's generated values from the reader's current row, the columns in order from ordinal 0.</summary>
    public Action<T, DbDataReader, SqlDialect> Read { get; } = read;
}
