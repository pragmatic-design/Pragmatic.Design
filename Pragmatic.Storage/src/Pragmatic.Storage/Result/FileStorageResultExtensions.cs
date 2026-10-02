using Pragmatic.Result;

// ReSharper disable once CheckNamespace — the Result-based surface lives in the Pragmatic.Storage
// namespace so it is discovered alongside IFileStorage; the Result/ folder only groups it.
namespace Pragmatic.Storage;

/// <summary>
///     Result-based counterpart to the throwing <see cref="IFileStorage" /> API.
/// </summary>
/// <remarks>
///     <para>
///         These extensions wrap the throwing/null-returning methods of <see cref="IFileStorage" />
///         and return <see cref="Result{TValue,TError}" /> / <see cref="VoidResult{TError}" /> with
///         typed errors (<see cref="FileTooLargeError" />, <see cref="StorageFileNotFoundError" />,
///         <see cref="StorageWriteError" />). The original contract is unchanged; this is purely
///         additive, so a caller can pick the exception-based or the Result-based surface.
///     </para>
///     <para>
///         The error slot is <see cref="IError" /> (matching the Actions/Mutation path that returns
///         <c>Result&lt;T, IError&gt;</c>); the concrete error types convert implicitly. An
///         <see cref="OperationCanceledException" /> is never converted to a failure — it propagates,
///         so cancellation stays distinct from an I/O error.
///     </para>
/// </remarks>
public static class FileStorageResultExtensions
{
    /// <param name="storage">The file storage instance.</param>
    extension(IFileStorage storage)
    {
        /// <summary>
        ///     Saves a file and returns a <see cref="Result{TValue,TError}" /> with the stored URI,
        ///     or a typed error instead of throwing.
        /// </summary>
        /// <param name="content">The file content stream.</param>
        /// <param name="fileName">Original file name (used for extension / content-type detection).</param>
        /// <param name="container">Logical container or folder (e.g. "photos", "imports").</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        ///     A Result with the stored URI on success, or a failure carrying
        ///     <see cref="FileTooLargeError" /> (oversized upload) or <see cref="StorageWriteError" />
        ///     (invalid container/path or a provider I/O failure).
        /// </returns>
        /// <example>
        ///     <code>
        /// // In a Mutation returning Result&lt;Uri, IError&gt;
        /// var result = await _storage.SaveAsResultAsync(photo.OpenReadStream(), photo.FileName, "photos", ct);
        /// return result;
        ///     </code>
        /// </example>
        public async Task<Result<Uri, IError>> SaveAsResultAsync(
            Stream content,
            string fileName,
            string container,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(storage);

            try
            {
                var uri = await storage.SaveAsync(content, fileName, container, ct).ConfigureAwait(false);
                return Result<Uri, IError>.Success(uri);
            }
            catch (FileSizeLimitExceededException e)
            {
                return FileTooLargeError.Create(e.LimitBytes, e.ActualBytes);
            }
            catch (ArgumentException e)
            {
                // Invalid container/path (e.g. traversal segment) — a caller error, not an I/O fault.
                return StorageWriteError.Create(e.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return StorageWriteError.From(e);
            }
        }

        /// <summary>
        ///     Opens a read stream to a stored file, returning a typed
        ///     <see cref="StorageFileNotFoundError" /> instead of a <see langword="null" /> stream.
        /// </summary>
        /// <param name="fileUri">The URI returned by <see cref="SaveAsResultAsync" />.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        ///     A Result with a readable stream on success (the caller must dispose it), or a failure
        ///     carrying <see cref="StorageFileNotFoundError" /> (missing file) or
        ///     <see cref="StorageWriteError" /> (provider I/O failure).
        /// </returns>
        /// <example>
        ///     <code>
        /// var result = await _storage.GetAsResultAsync(fileUri, ct);
        /// return result;
        ///     </code>
        /// </example>
        public async Task<Result<Stream, IError>> GetAsResultAsync(Uri fileUri, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(storage);

            try
            {
                var stream = await storage.GetAsync(fileUri, ct).ConfigureAwait(false);
                if (stream is null)
                    return StorageFileNotFoundError.For(fileUri);
                return Result<Stream, IError>.Success(stream);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return StorageWriteError.From(e);
            }
        }

        /// <summary>
        ///     Checks whether a file exists, returning the boolean as a Result and wrapping any
        ///     provider failure in <see cref="StorageWriteError" />.
        /// </summary>
        /// <param name="fileUri">The URI returned by <see cref="SaveAsResultAsync" />.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        ///     A Result with <see langword="true" />/<see langword="false" /> on success, or a failure
        ///     carrying <see cref="StorageWriteError" />. A missing file is a successful
        ///     <see langword="false" />, not a failure.
        /// </returns>
        /// <example>
        ///     <code>
        /// var result = await _storage.ExistsAsResultAsync(fileUri, ct);
        /// return result;
        ///     </code>
        /// </example>
        public async Task<Result<bool, IError>> ExistsAsResultAsync(Uri fileUri, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(storage);

            try
            {
                return await storage.ExistsAsync(fileUri, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return StorageWriteError.From(e);
            }
        }

        /// <summary>
        ///     Deletes a stored file, returning a <see cref="VoidResult{TError}" />. Deletion is
        ///     idempotent, so a missing file is a success.
        /// </summary>
        /// <param name="fileUri">The URI returned by <see cref="SaveAsResultAsync" />.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        ///     A successful VoidResult, or a failure carrying <see cref="StorageWriteError" /> on a
        ///     provider I/O failure.
        /// </returns>
        /// <example>
        ///     <code>
        /// var result = await _storage.DeleteAsResultAsync(fileUri, ct);
        /// return result;
        ///     </code>
        /// </example>
        public async Task<VoidResult<IError>> DeleteAsResultAsync(Uri fileUri, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(storage);

            try
            {
                await storage.DeleteAsync(fileUri, ct).ConfigureAwait(false);
                return VoidResult<IError>.Success();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return StorageWriteError.From(e);
            }
        }
    }
}
