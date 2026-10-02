using Pragmatic.Result;

// ReSharper disable once CheckNamespace — the Result-based surface lives in the Pragmatic.Storage
// namespace so it is discovered alongside ISignedUrlProvider; the Result/ folder only groups it.
namespace Pragmatic.Storage;

/// <summary>
///     Result-based counterpart to the throwing <see cref="ISignedUrlProvider" /> API.
/// </summary>
/// <remarks>
///     Wraps <see cref="ISignedUrlProvider.GetDownloadUrlAsync" /> into a
///     <see cref="Result{TValue,TError}" />. A <see cref="NotSupportedException" /> (a provider that
///     cannot sign URLs) and any other provider failure both become a <see cref="StorageWriteError" />
///     carrying the reason. An <see cref="OperationCanceledException" /> is never converted to a
///     failure — it propagates.
/// </remarks>
public static class SignedUrlResultExtensions
{
    /// <param name="provider">The signed-URL provider.</param>
    extension(ISignedUrlProvider provider)
    {
        /// <summary>
        ///     Generates a temporary download URL as a <see cref="Result{TValue,TError}" />, wrapping a
        ///     <see cref="NotSupportedException" /> or provider failure in
        ///     <see cref="StorageWriteError" /> instead of throwing.
        /// </summary>
        /// <param name="fileUri">The URI returned by <see cref="IFileStorage.SaveAsync" />.</param>
        /// <param name="expiry">How long the generated URL remains valid, measured from now.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        ///     A Result with the signed URL on success, or a failure carrying
        ///     <see cref="StorageWriteError" /> — whose <see cref="StorageWriteError.Reason" /> explains
        ///     why signing was unavailable when the provider does not support it.
        /// </returns>
        /// <example>
        ///     <code>
        /// var result = await provider.GetDownloadUrlAsResultAsync(fileUri, TimeSpan.FromMinutes(15), ct);
        /// return result.Match(url => Redirect(url.ToString()), error => Problem(error));
        ///     </code>
        /// </example>
        public async Task<Result<Uri, IError>> GetDownloadUrlAsResultAsync(
            Uri fileUri,
            TimeSpan expiry,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(provider);

            try
            {
                var url = await provider.GetDownloadUrlAsync(fileUri, expiry, ct).ConfigureAwait(false);
                return Result<Uri, IError>.Success(url);
            }
            catch (NotSupportedException e)
            {
                // The provider cannot sign URLs (e.g. no shared key credential) — surface the reason.
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
    }
}
