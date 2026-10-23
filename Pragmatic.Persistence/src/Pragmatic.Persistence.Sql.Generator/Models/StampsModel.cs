namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>The columns a root writer stamps: <c>[Auditable]</c>'s created and updated pairs, <c>[SoftDelete]</c>'s flag and its time and user.</summary>
internal sealed record StampsModel(
    string EntityType,
    StampColumnModel? CreatedAt,
    StampColumnModel? CreatedBy,
    StampColumnModel? UpdatedAt,
    StampColumnModel? UpdatedBy,
    StampColumnModel? IsDeleted,
    StampColumnModel? DeletedAt,
    StampColumnModel? DeletedBy);
