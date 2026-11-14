namespace Pragmatic.Persistence.Entity;

/// <summary>
///     An entity whose unique indexes declare the code their violation answers with (<c>[Unique(ErrorCode = …)]</c>, #728). The generator
///     implements it; every engine asks the entity it wrote, once it has told which index refused the write.
/// </summary>
public interface IDeclaresUniqueErrorCodes
{
    /// <summary>
    ///     The code declared on the unique index a duplicate broke, or <see langword="null" /> when that index declared none, or none of
    ///     this entity's indexes is the one reported.
    /// </summary>
    /// <param name="fields">The properties the engine found the index covers, joined with <c>", "</c> in the index's order; null when unknown.</param>
    /// <param name="constraint">The index name the database reported (<c>IX_{Table}_{Columns}</c>); null when it named none.</param>
    string? UniqueErrorCode(string? fields, string? constraint);
}
