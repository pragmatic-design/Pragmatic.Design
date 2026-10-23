using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

/// <summary>A row with an embedding: pgvector <c>vector(16)</c>, SQL Server 2025 <c>VECTOR(16)</c>, JSON text on SQLite; the k-NN tests' table.</summary>
[Entity]
public sealed class Embedding : ISqlEntity
{
    public const int Dimensions = 16;

    public Guid Id { get; set; }

    public string Label { get; set; } = "";

    [Vector(Dimensions)]
    public float[] Vector { get; set; } = [];
}
