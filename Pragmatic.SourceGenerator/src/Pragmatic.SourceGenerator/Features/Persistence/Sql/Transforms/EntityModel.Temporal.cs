using System.Linq;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

internal sealed partial class EntityModel
{
    /// <summary><c>[Temporal]</c>: on PostgreSQL and SQLite, AsOf reads the <c>{Table}History</c> table its triggers keep (SqlTemporal).</summary>
    public bool Temporal => Type.GetAttributes().Any(a => a.AttributeClass?.Name == "TemporalAttribute");
}
