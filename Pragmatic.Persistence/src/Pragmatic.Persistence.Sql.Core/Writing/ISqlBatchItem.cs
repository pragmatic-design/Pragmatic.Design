using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>What the batch needs of an item, whatever its row type.</summary>
internal interface ISqlBatchItem
{
    int PlanId { get; }

    SqlParamKind[] Kinds { get; }

    /// <summary>True when the plan is one statement (no companion statements after it): what a provider batch command takes.</summary>
    bool SingleStatement { get; }

    string Sql(SqlDialect dialect);

    /// <summary>Binds this call's values into the batch command's parameters, from <paramref name="offset" /> on.</summary>
    void Bind(DbParameterCollection target, int offset, SqlDialect dialect);

    Task ReadAsync(DbDataReader reader, SqlDialect dialect, CancellationToken ct);
}
