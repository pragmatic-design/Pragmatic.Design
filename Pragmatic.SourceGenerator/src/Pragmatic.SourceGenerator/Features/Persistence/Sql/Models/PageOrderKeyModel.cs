using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A key a repository's <c>PageAsync</c> orders by (#946): the member an <c>Ordering&lt;T&gt;</c> names, the key's field name, and its texts
///     (one per dialect on the Sql engine, the MongoDB and Cosmos DB names on the documents engine).
/// </summary>
internal sealed record PageOrderKeyModel(string Member, string Key, EquatableArray<string> Texts);
