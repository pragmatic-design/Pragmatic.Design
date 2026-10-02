using Pragmatic.Result;

// ReSharper disable once CheckNamespace — the Result-based surface lives in the Pragmatic.Storage
// namespace so it is discovered alongside IFileInfoProvider; the Result/ folder only groups it.
namespace Pragmatic.Storage;

/// <summary>
///     Result-based counterpart to the throwing/null-returning <see cref="IFileInfoProvider" /> API.
/// </summary>
/// <remarks>
///     Wraps <see cref="IFileInfoProvider.GetInfoAsync" /> into a <see cref="Result{TValue,TError}" />:
///     a missing file becomes a typed <see cref="StorageFileNotFoundError" /> instead of
///     <see langword="null" />, and a provider failure becomes a <see cref="StorageWriteError" />. An
///     <see cref="OperationCanceledException" /> is never converted to a failure — it propagates.
/// </remarks>
public static class FileInfoResultExtensions
{
    /// <param name="provider">The file-info provider.</param>
    extension(IFileInfoProvider provider)
    {
        /// <summary>
        ///     Returns a stored file's metadata as a <see cref="Result{TValue,TError}" />, mapping a
        ///     missing file to <see cref="StorageFileNotFoundError" /> instead of a
        ///     <see langword="null" /> result.
        /// </summary>
        /// <param name="fileUri">The URI returned by <see cref="IFileStorage.SaveAsync" />.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        ///     A Result with the <see cref="StoredFileInfo" /> on success, or a failure carrying
        ///     <see cref="StorageFileNotFoundError" /> (missing file) or <see cref="StorageWriteError" />
        ///     (provider I/O failure).
        /// </returns>
        /// <example>
        ///     <code>
        /// var result = await provider.GetInfoAsResultAsync(fileUri, ct);
        /// return result.Match(info => Ok(info), error => Problem(error));
        ///     </code>
        /// </example>
        public async Task<Result<StoredFileInfo, IError>> GetInfoAsResultAsync(
            Uri fileUri,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(provider);

            try
            {
                var info = await provider.GetInfoAsync(fileUri, ct).ConfigureAwait(false);
                if (info is null)
                    return StorageFileNotFoundError.For(fileUri);
                return Result<StoredFileInfo, IError>.Success(info);
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
