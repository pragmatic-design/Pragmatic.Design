using Microsoft.Extensions.Logging;
using Pragmatic.MultiTenancy;
using Pragmatic.Storage;

namespace Pragmatic.Messaging.ClaimCheck;

/// <summary>
///     <see cref="IClaimCheckStore"/> on <see cref="IFileStorage"/> (local disk, Azure Blob, S3 —
///     whatever the app registered). The reference IS the storage URI.
/// </summary>
public sealed partial class FileStorageClaimCheckStore(
    IFileStorage storage,
    ILogger<FileStorageClaimCheckStore> logger,
    ITenantContext? tenantContext = null) : IClaimCheckStore
{
    /// <summary>Logical storage container for checked payloads.</summary>
    public const string Container = "claim-checks";

    /// <inheritdoc />
    public async Task<string> StoreAsync(Stream payload, CancellationToken ct = default)
    {
        // Tenant-prefix the blob so a checked payload lives under its tenant's segment and a wire
        // reference from another tenant is rejected on retrieve. Empty prefix in single-tenant hosts.
        var tenant = tenantContext?.TenantId;
        var name = string.IsNullOrEmpty(tenant)
            ? $"{Guid.NewGuid():N}.bin"
            : $"{tenant}/{Guid.NewGuid():N}.bin";
        // Stream straight to storage — no intermediate buffer (the payload is >256KiB by definition).
        // The caller owns the passed-in stream; we only read it.
        var uri = await storage.SaveAsync(payload, name, Container, ct).ConfigureAwait(false);
        return uri.ToString();
    }

    /// <inheritdoc />
    public async Task<Stream> RetrieveAsync(string reference, CancellationToken ct = default)
    {
        // Hand back the storage stream directly — the consumer deserializes from it without a copy.
        // The CALLER disposes it (see TransportSubscriptionBinder).
        return await storage.GetAsync(ValidateReference(reference), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Claim-checked payload not found at '{reference}' — deleted before consume? " +
                "With redeliveries in play, disable ClaimCheckOptions.DeleteAfterConsume or add store retention.");
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            await storage.DeleteAsync(ValidateReference(reference), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cleanup is best-effort: an orphaned blob costs storage, never correctness.
            LogDeleteFailed(reference, ex);
        }
    }

    /// <summary>
    ///     Validates an inbound claim-check reference before it reaches the storage provider.
    /// </summary>
    /// <remarks>
    ///     The reference travels on the wire in the <c>x-claim-check</c> header, so anyone who can
    ///     publish or tamper with a broker message controls it. Only the exact shape
    ///     <see cref="StoreAsync"/> produces is accepted — <c>&lt;container&gt;/&lt;32-hex&gt;.bin</c>
    ///     (single-tenant) or <c>&lt;container&gt;/&lt;tenant&gt;/&lt;32-hex&gt;.bin</c> (multi-tenant) —
    ///     which rejects path traversal, reads of other containers, and arbitrary file paths.
    ///     Blob names are server-generated GUIDs, so a reference cannot be guessed onto another payload.
    ///     When a tenant is resolved, the reference's tenant segment MUST equal the current tenant, so a
    ///     wire reference from tenant A cannot read tenant B's blob. (Residual: an app whose
    ///     <c>IFileStorage</c> resolves foreign absolute URIs should restrict that at the provider level —
    ///     this store cannot know its own base address.)
    /// </remarks>
    private Uri ValidateReference(string reference)
    {
        if (!Uri.TryCreate(reference, UriKind.RelativeOrAbsolute, out var uri))
            throw new InvalidOperationException("Malformed claim-check reference.");

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : reference;
        var segments = path.Split('/', '\\');

        // Two accepted shapes: [..., container, blob] (single-tenant) or
        // [..., container, tenant, blob] (multi-tenant). Anything else is rejected.
        string? referenceTenant;
        if (segments.Length >= 2 && string.Equals(segments[^2], Container, StringComparison.Ordinal))
            referenceTenant = null;
        else if (segments.Length >= 3 && string.Equals(segments[^3], Container, StringComparison.Ordinal))
            referenceTenant = segments[^2];
        else
            referenceTenant = string.Empty; // sentinel: shape mismatch

        if (referenceTenant == string.Empty || !IsGeneratedBlobName(segments[^1]))
        {
            throw new InvalidOperationException(
                "Rejected claim-check reference: it does not match the expected " +
                $"'{Container}/[<tenant>/]<id>.bin' shape. The reference is attacker-controllable, so only " +
                "payloads this store wrote are retrievable.");
        }

        // Tenant enforcement: a resolved tenant may read only its own blobs; an unresolved tenant may
        // read only tenant-less blobs. This closes cross-tenant reads via a forged wire reference.
        var currentTenant = tenantContext?.TenantId;
        var expectedTenant = string.IsNullOrEmpty(currentTenant) ? null : currentTenant;
        if (!string.Equals(referenceTenant, expectedTenant, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Rejected claim-check reference: its tenant segment does not match the current tenant. " +
                "The reference is attacker-controllable, so cross-tenant blob access is denied.");
        }

        return uri;
    }

    /// <summary>True for the exact <c>{Guid:N}.bin</c> name shape <see cref="StoreAsync"/> writes.</summary>
    private static bool IsGeneratedBlobName(string name)
    {
        const int hexLength = 32;
        if (name.Length != hexLength + 4 || !name.EndsWith(".bin", StringComparison.Ordinal))
            return false;

        for (var i = 0; i < hexLength; i++)
        {
            var c = name[i];
            if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claim-check cleanup failed for {Reference} (orphaned blob — storage cost only)")]
    private partial void LogDeleteFailed(string reference, Exception ex);
}
