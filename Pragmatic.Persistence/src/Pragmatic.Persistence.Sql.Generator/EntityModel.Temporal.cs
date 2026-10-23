using System.Linq;

namespace Pragmatic.Persistence.Sql.Generator;

internal sealed partial class EntityModel
{
    /// <summary><c>[Temporal]</c>: on PostgreSQL and SQLite, AsOf reads the <c>{Table}History</c> table its triggers keep (SqlTemporal).</summary>
    public bool Temporal => Type.GetAttributes().Any(a => a.AttributeClass?.Name == "TemporalAttribute");
}
