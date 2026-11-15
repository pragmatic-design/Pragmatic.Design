namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A relation's <c>OnSoftDelete</c> as the Sql engine registers it at module start: what soft-deleting <see cref="PrincipalType" /> does to the
///     <see cref="DependentType" /> rows whose <see cref="ForeignKeyProperty" /> holds its key.
/// </summary>
/// <param name="PrincipalType">The principal, fully qualified.</param>
/// <param name="DependentType">The referrer, fully qualified.</param>
/// <param name="Behavior">Pragmatic's <c>DeleteBehavior</c> member name.</param>
/// <param name="ForeignKeyColumn">The referrer's foreign key column.</param>
/// <param name="ForeignKeyProperty">The referrer's foreign key property.</param>
/// <param name="Kind">The key's <c>SqlParamKind</c> member name.</param>
/// <param name="ClearKey">The statement that empties the key on <c>d</c> (the referrer, boxed), for <c>SetNull</c>; null otherwise.</param>
internal sealed record SoftDeleteRuleMapModel(
    string PrincipalType,
    string DependentType,
    string Behavior,
    string ForeignKeyColumn,
    string ForeignKeyProperty,
    string Kind,
    string? ClearKey);
