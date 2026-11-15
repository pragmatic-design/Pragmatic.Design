namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>
///     A relation's <c>OnSoftDelete</c> as the documents engine registers it at module start, from the referrer's side: what soft-deleting
///     <see cref="PrincipalType" /> does to this entity's documents whose <see cref="ForeignKeyProperty" /> holds its key.
/// </summary>
/// <param name="PrincipalType">The principal, fully qualified.</param>
/// <param name="Behavior">Pragmatic's <c>DeleteBehavior</c> member name.</param>
/// <param name="ForeignKeyField">The foreign key's field in a MongoDB document.</param>
/// <param name="ForeignKeyJson">The foreign key's name in Cosmos DB's JSON.</param>
/// <param name="ForeignKeyProperty">The referrer's foreign key property.</param>
/// <param name="ClearsKey">The rule empties the key (<c>SetNull</c>, <c>ClientSetNull</c>).</param>
internal sealed record DocSoftDeleteRuleModel(
    string PrincipalType,
    string Behavior,
    string ForeignKeyField,
    string ForeignKeyJson,
    string ForeignKeyProperty,
    bool ClearsKey);
