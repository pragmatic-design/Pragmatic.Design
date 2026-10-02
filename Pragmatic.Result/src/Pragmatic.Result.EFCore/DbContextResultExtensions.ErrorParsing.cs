using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Registry resolution and mapping from parsed <see cref="DbErrorInfo" /> to Result error types.
/// </summary>
public static partial class DbContextResultExtensions
{
    /// <summary>
    ///     Resolves the <see cref="DbExceptionParserRegistry" /> from the context's application
    ///     services (registered via <c>Add{Provider}ResultErrorHandling</c>), or
    ///     <see cref="DbExceptionParserRegistry.Default" /> when none is available.
    /// </summary>
    /// <remarks>
    ///     The registry is an application service, so it is retrieved through the application service
    ///     provider carried on <see cref="CoreOptionsExtension" /> rather than EF Core's internal
    ///     provider. Contexts constructed without DI (e.g. <c>new DbContext(options)</c>) have no
    ///     application provider and fall back to the heuristic-only default.
    /// </remarks>
    private static DbExceptionParserRegistry ResolveRegistry(DbContext context)
    {
        var appServices = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider;

        return appServices?.GetService<DbExceptionParserRegistry>() ?? DbExceptionParserRegistry.Default;
    }

    /// <summary>
    ///     Classifies a <see cref="DbUpdateException" /> into the rule it violated, as a bare error.
    /// </summary>
    /// <remarks>
    ///     The shared body of <see cref="ClassifyConflictOrConstraint" /> and the public
    ///     <c>ClassifyRuleViolation</c>: one wraps the result in a <c>VoidResult</c>, the other hands it
    ///     to an exception. The classification must not differ between them, so there is one of it.
    /// </remarks>
    private static IError ClassifyRuleViolationError(
        DbUpdateException ex, DbExceptionParserRegistry registry)
    {
        var info = registry.Parse(ex);
        var entityType = GetEntityTypeFromException(ex);

        return info.ErrorType switch
        {
            // ⚠️ Both, because a provider fills one or the other and rarely the same one: PostgreSQL
            // leaves ColumnName empty for 23505 and names the constraint.
            DbErrorType.UniqueConstraint => DbConflictError.UniqueViolation(
                entityType, info.ColumnName, info.ConstraintName),
            DbErrorType.ForeignKeyConstraint => DbConstraintError.ForeignKeyViolation(
                info.TableName ?? entityType, info.ConstraintName),
            // Only conflict and constraint are produced here, so null/max-length/numeric/check and any
            // transient classification collapse to a generic 400 constraint error. Callers needing the
            // full taxonomy use SaveChangesDetailedAsResultAsync.
            _ => DbConstraintError.FromDetails(info.Details ?? ex.InnerException?.Message ?? ex.Message)
        };
    }

    /// <summary>
    ///     Maps a <see cref="DbUpdateException" /> to the constrained conflict/constraint pair used by
    ///     <c>SaveChangesAsResultAsync</c>, classifying via the supplied registry.
    /// </summary>
    private static VoidResult<DbConflictError, DbConstraintError> ClassifyConflictOrConstraint(
        DbUpdateException ex, DbExceptionParserRegistry registry)
    {
        // The switch above returns one of exactly these two, so the cast cannot fail.
        var error = ClassifyRuleViolationError(ex, registry);
        return error is DbConflictError conflict ? conflict : (DbConstraintError)error;
    }

    private static VoidResult<DbConflictError, DbNullConstraintError, DbMaxLengthError, DbNumericOverflowError,
            DbConstraintError, DbTransientError>
        ParseDbUpdateException(DbUpdateException ex, DbExceptionParserRegistry parserRegistry)
    {
        var entityType = GetEntityTypeFromException(ex);
        var info = parserRegistry.Parse(ex);

        return info.ErrorType switch
        {
            // ⚠️ Both, because a provider fills one or the other and rarely the same one: PostgreSQL
            // leaves ColumnName empty for 23505 and names the constraint.
            DbErrorType.UniqueConstraint => DbConflictError.UniqueViolation(
                entityType, info.ColumnName, info.ConstraintName),
            DbErrorType.ForeignKeyConstraint => DbConstraintError.ForeignKeyViolation(info.TableName ?? entityType,
                info.ConstraintName),
            DbErrorType.NullConstraint => DbNullConstraintError.Create(info.TableName ?? entityType, info.ColumnName),
            DbErrorType.MaxLengthExceeded => DbMaxLengthError.Create(info.TableName ?? entityType, info.ColumnName,
                info.MaxLength),
            DbErrorType.NumericOverflow => DbNumericOverflowError.Create(info.TableName ?? entityType, info.ColumnName,
                info.Details),
            DbErrorType.CheckConstraint => DbConstraintError.FromDetails(info.Details),
            DbErrorType.Deadlock => DbTransientError.Deadlock(),
            DbErrorType.Timeout => DbTransientError.Timeout(info.Details),
            DbErrorType.ConnectionFailure => DbTransientError.ConnectionFailure(info.Details),
            _ => DbConstraintError.FromDetails(info.Details ?? ex.InnerException?.Message ?? ex.Message)
        };
    }
}
