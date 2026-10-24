namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On a <c>float[]</c> property: a vector of <see cref="Dimensions" /> (pgvector <c>vector(n)</c>, SQL Server 2025 <c>VECTOR(n)</c>, JSON text on
///     SQLite). <c>Sql.CosineDistance</c>, <c>Sql.L2Distance</c> and <c>Sql.InnerProduct</c> compare it with another vector (a k-NN is an
///     OrderBy on the distance and a Take); on SQLite they call the application's functions, <see cref="SqlVectors" /> registered on the connection.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class VectorAttribute(int dimensions) : Attribute
{
    public int Dimensions { get; } = dimensions;
}
