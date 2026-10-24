using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace PragmaticEntities.Tests;

/// <summary>
///     What Pragmatic's mutation invoker does around a write (<c>MutationInvokerTemplate</c> and <c>MutationInvoker.Pipeline</c>), on the
///     boundary's repository and unit of work, which on the Sql engine are <c>{Entity}.SqlRepository</c> and <see cref="Pragmatic.Persistence.Sql.SqlUnitOfWork" />:
///     a create adds and saves; an update loads by key, applies, saves; a failed save detaches the entity and the failure goes on, as the
///     invoker's <c>catch { UnitOfWork?.Detach(entity); throw; }</c> does. The validation, the permission and the events around them are the
///     invoker base's and do not change with the engine.
/// </summary>
/// <remarks>TODO: written by hand, until the mutation templates emit these calls for every engine (step 8 of the persistence engines' port).</remarks>
internal static class AmenityMutations
{
    /// <summary>A create: <c>PersistNew</c> (<c>_repository.Add</c>), then <c>_unitOfWork.SaveChangesAsync</c>.</summary>
    public static async Task<Amenity> CreateAsync(IRepository<Amenity> repository, IUnitOfWork unitOfWork, string name, CancellationToken ct = default)
    {
        var entity = new Amenity { Name = name };
        repository.Add(entity);
        await SaveAsync(unitOfWork, entity, ct).ConfigureAwait(false);
        return entity;
    }

    /// <summary>An update: the entity loaded by key (404 when the caller cannot read it), the change applied, the save.</summary>
    public static async Task<Result<Amenity, IError>> RenameAsync(IRepository<Amenity> repository, IUnitOfWork unitOfWork, Guid id, string name, CancellationToken ct = default)
    {
        var entity = await repository.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (entity is null)
            return Result<Amenity, IError>.Failure(NotFoundError.For("Amenity", id.ToString()));
        entity.Name = name;
        await SaveAsync(unitOfWork, entity, ct).ConfigureAwait(false);
        return Result<Amenity, IError>.Success(entity);
    }

    private static async Task SaveAsync(IUnitOfWork unitOfWork, Amenity entity, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            unitOfWork.Detach(entity);
            throw;
        }
    }
}
