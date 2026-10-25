namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A database whose <c>{Database}Schema</c> the persistence generator wrote, and the namespace it wrote it in: what the host names when it
///     migrates the database, read from the writer rather than derived again (the namespace follows the database's entities, which the host
///     does not see).
/// </summary>
/// <param name="DatabaseName">The database's class name, without its namespace (<c>ShowcaseAppDatabase</c>).</param>
/// <param name="Namespace">The namespace the schema is declared in.</param>
internal sealed record SchemaDatabaseModel(string DatabaseName, string Namespace)
{
    /// <summary>The schema's type, fully qualified.</summary>
    public string SchemaTypeName => string.IsNullOrEmpty(Namespace) ? $"global::{DatabaseName}Schema" : $"global::{Namespace}.{DatabaseName}Schema";
}
