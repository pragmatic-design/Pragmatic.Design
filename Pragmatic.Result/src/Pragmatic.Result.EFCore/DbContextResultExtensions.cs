using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Extension methods for DbContext that return Result types.
/// </summary>
/// <remarks>
///     <para>
///         These extensions wrap SaveChangesAsync to return Result types instead of
///         throwing exceptions, enabling functional error handling.
///     </para>
///     <para>
///         Classification uses the <see cref="DbExceptionParserRegistry" /> resolved from the
///         context's application services when a provider package
///         (<c>Add{Provider}ResultErrorHandling</c>) has been registered, and falls back to the
///         cross-provider heuristic parser otherwise.
///     </para>
///     <para>
///         Inspired by EntityFramework.Exceptions (https://github.com/Giorgi/EntityFramework.Exceptions)
///         but using Result types instead of exceptions.
///     </para>
///     <para>
///         This is a partial class split across multiple files for maintainability:
///         <list type="bullet">
///             <item>
///                 <description>DbContextResultExtensions.cs - SaveChanges extension methods (this file)</description>
///             </item>
///             <item>
///                 <description>DbContextResultExtensions.ErrorParsing.cs - Registry resolution and error mapping</description>
///             </item>
///             <item>
///                 <description>DbContextResultExtensions.EntityHelpers.cs - Entity information extraction</description>
///             </item>
///         </list>
///     </para>
/// </remarks>
public static partial class DbContextResultExtensions
{
    /// <param name="context">The DbContext instance.</param>
    extension(DbContext context)
    {
        /// <summary>
        ///     Saves all changes made in this context and returns a Result.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        ///     A VoidResult that succeeds if SaveChanges completes, or fails with
        ///     <see cref="DbConflictError" /> for concurrency/unique violations, or
        ///     <see cref="DbConstraintError" /> for other constraint violations.
        /// </returns>
        /// <remarks>
        ///     Constraint classification honors the provider parser registered via
        ///     <c>Add{Provider}ResultErrorHandling</c> (resolved from the context's application
        ///     services); when none is registered it falls back to the heuristic parser.
        /// </remarks>
        /// <example>
        ///     <code>
        /// var saveResult = await context.SaveChangesAsResultAsync();
        /// return saveResult.Match(
        ///     () => Ok(),
        ///     conflict => Conflict(conflict.Reason),
        ///     constraint => BadRequest(constraint.Details));
        /// </code>
        /// </example>
        public async Task<VoidResult<DbConflictError, DbConstraintError>> SaveChangesAsResultAsync(CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return VoidResult<DbConflictError, DbConstraintError>.Success();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var entry = GetFirstEntry(ex.Entries);
                var entityType = entry?.Entity.GetType().Name ?? "Unknown";
                var entityId = GetEntityId(entry);

                return DbConflictError.ConcurrencyConflict(entityType, entityId);
            }
            catch (DbUpdateException ex)
            {
                return ClassifyConflictOrConstraint(ex, ResolveRegistry(context));
            }
        }

        /// <summary>
        ///     Classifies a <see cref="DbUpdateException" /> that has already been caught, for a caller
        ///     that must keep the exception rather than replace it with a Result.
        /// </summary>
        /// <param name="exception">The provider exception to classify.</param>
        /// <returns>The rule that was violated, as a domain error.</returns>
        /// <remarks>
        ///     <para>
        ///         <c>EfCoreUnitOfWork</c> is the caller: it throws
        ///         <c>PersistenceRuleViolationException</c> carrying both this error and the exception it
        ///         came from, because <c>SaveChangesAsync</c> returns a row count and every layer between
        ///         it and the action would otherwise have to thread a failure it cannot act on.
        ///     </para>
        ///     <para>
        ///         <b>Catch <see cref="DbUpdateConcurrencyException" /> separately and do not pass it
        ///         here.</b> A stale row is two writers meeting, not a rule the schema enforces, and both
        ///         land on <see cref="DbConflictError" /> — so classifying it makes a lost update
        ///         indistinguishable from a duplicate key.
        ///     </para>
        /// </remarks>
        public IError ClassifyRuleViolation(DbUpdateException exception)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(exception);

            return ClassifyRuleViolationError(exception, ResolveRegistry(context));
        }

        /// <summary>
        ///     Saves all changes and returns the number of affected rows on success.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        ///     A Result with the number of affected rows on success, or an error on failure.
        /// </returns>
        /// <remarks>
        ///     Constraint classification honors the provider parser registered via
        ///     <c>Add{Provider}ResultErrorHandling</c>; when none is registered it falls back to the
        ///     heuristic parser.
        /// </remarks>
        public async Task<Result<int, DbConflictError, DbConstraintError>> SaveChangesWithCountAsResultAsync(CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            try
            {
                var count = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return count;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var entry = GetFirstEntry(ex.Entries);
                var entityType = entry?.Entity.GetType().Name ?? "Unknown";
                var entityId = GetEntityId(entry);

                return DbConflictError.ConcurrencyConflict(entityType, entityId);
            }
            catch (DbUpdateException ex)
            {
                var info = ResolveRegistry(context).Parse(ex);
                var entityType = GetEntityTypeFromException(ex);

                return info.ErrorType switch
                {
                    DbErrorType.UniqueConstraint => DbConflictError.UniqueViolation(entityType, info.ColumnName),
                    DbErrorType.ForeignKeyConstraint => DbConstraintError.ForeignKeyViolation(
                        info.TableName ?? entityType, info.ConstraintName),
                    _ => DbConstraintError.FromDetails(info.Details ?? ex.InnerException?.Message ?? ex.Message)
                };
            }
        }

        /// <summary>
        ///     Saves all changes with detailed error classification.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        ///     A VoidResult with specific error types for each failure category:
        ///     <list type="bullet">
        ///         <item><see cref="DbConflictError" /> - concurrency or unique constraint violations</item>
        ///         <item><see cref="DbNullConstraintError" /> - NOT NULL constraint violations</item>
        ///         <item><see cref="DbMaxLengthError" /> - max length exceeded</item>
        ///         <item><see cref="DbNumericOverflowError" /> - numeric overflow</item>
        ///         <item><see cref="DbConstraintError" /> - other constraint violations</item>
        ///         <item><see cref="DbTransientError" /> - transient errors (deadlocks, timeouts)</item>
        ///     </list>
        /// </returns>
        /// <remarks>
        ///     Resolves the provider parser registry from the context's application services when
        ///     available (registered via <c>Add{Provider}ResultErrorHandling</c>), falling back to the
        ///     heuristic parser. Use the <c>SaveChangesDetailedAsResultAsync(DbExceptionParserRegistry)</c>
        ///     overload to supply a registry explicitly.
        /// </remarks>
        /// <example>
        ///     <code>
        /// var result = await context.SaveChangesDetailedAsResultAsync();
        /// return result.Match(
        ///     () => Ok(),
        ///     conflict => Conflict(conflict.Reason),
        ///     nullError => BadRequest($"Required field missing: {nullError.ColumnName}"),
        ///     maxLength => BadRequest($"Value too long: {maxLength.ColumnName}"),
        ///     overflow => BadRequest($"Numeric overflow: {overflow.ColumnName}"),
        ///     constraint => BadRequest(constraint.Details),
        ///     transient => StatusCode(503, "Please retry"));
        /// </code>
        /// </example>
        public Task<VoidResult<DbConflictError, DbNullConstraintError, DbMaxLengthError, DbNumericOverflowError,
                DbConstraintError, DbTransientError>>
            SaveChangesDetailedAsResultAsync(CancellationToken cancellationToken = default)
        {
            return context.SaveChangesDetailedAsResultAsync(ResolveRegistry(context), cancellationToken);
        }

        /// <summary>
        ///     Saves all changes with detailed error classification using a custom parser registry.
        /// </summary>
        /// <param name="parserRegistry">The exception parser registry to use.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        ///     A VoidResult with specific error types for each failure category.
        /// </returns>
        /// <remarks>
        ///     Use this overload with a provider-specific parser for more accurate error detection.
        ///     Provider packages register their parsers via DI and provide the registry.
        /// </remarks>
        public async Task<VoidResult<DbConflictError, DbNullConstraintError, DbMaxLengthError, DbNumericOverflowError
                , DbConstraintError, DbTransientError>>
            SaveChangesDetailedAsResultAsync(DbExceptionParserRegistry parserRegistry,
                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parserRegistry);

            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return VoidResult<DbConflictError, DbNullConstraintError, DbMaxLengthError, DbNumericOverflowError,
                    DbConstraintError, DbTransientError>.Success();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var entry = GetFirstEntry(ex.Entries);
                var entityType = entry?.Entity.GetType().Name ?? "Unknown";
                var entityId = GetEntityId(entry);

                return DbConflictError.ConcurrencyConflict(entityType, entityId);
            }
            catch (DbUpdateException ex)
            {
                return ParseDbUpdateException(ex, parserRegistry);
            }
            catch (Exception ex)
            {
                // Only classify transient failures the return type can represent.
                // Other categories (constraint/validation) only arrive via DbUpdateException,
                // so a bare exception classified as anything else is genuinely unhandled here.
                var info = parserRegistry.Parse(ex);
                switch (info.ErrorType)
                {
                    case DbErrorType.ConnectionFailure:
                        return DbTransientError.ConnectionFailure(info.Details);
                    case DbErrorType.Deadlock:
                        return DbTransientError.Deadlock();
                    case DbErrorType.Timeout:
                        return DbTransientError.Timeout(info.Details);
                    default:
                        // Unclassifiable / non-transient: do not swallow — re-throw preserving
                        // the original stack so transient or unexpected errors are not hidden.
                        throw;
                }
            }
        }
    }
}
