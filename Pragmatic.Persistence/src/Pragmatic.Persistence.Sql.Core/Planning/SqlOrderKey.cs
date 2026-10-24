using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>A column to order by, compiled for every dialect on the row alias <c>t</c>: a key of <see cref="SqlOrdering{T}" />.</summary>
public sealed class SqlOrderKey(string sqlite, string postgreSql, string sqlServer, string mySql, string oracle)
{
    private readonly string[] _texts = [sqlite, postgreSql, sqlServer, mySql, oracle];

    public string Text(SqlDialect dialect) => _texts[(int)dialect];
}
