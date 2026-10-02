using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Messaging.Configuration;

namespace Pragmatic.Messaging.ClaimCheck;

/// <summary>
///     Extension methods enabling the claim check pattern on <see cref="MessagingBuilder"/>.
/// </summary>
public static class ClaimCheckMessagingExtensions
{
    /// <summary>
    ///     Payloads above <see cref="ClaimCheckOptions.Threshold"/> are stored on the app's
    ///     <see cref="Pragmatic.Storage.IFileStorage"/> and the message carries only the
    ///     <c>x-claim-check</c> reference; the consume side retrieves transparently before
    ///     deserialization (and deletes after success when
    ///     <see cref="ClaimCheckOptions.DeleteAfterConsume"/>). Requires a registered
    ///     <c>IFileStorage</c> (UseStorage / AddLocalDiskStorage / Azure / S3).
    /// </summary>
    public static MessagingBuilder EnableClaimCheck(
        this MessagingBuilder builder,
        Action<ClaimCheckOptions>? configure = null)
    {
        var options = new ClaimCheckOptions();
        configure?.Invoke(options);

        builder.Services.TryAddSingleton(options);
        // Scoped (not Singleton): the store reads the ambient ITenantContext (scoped) to tenant-prefix
        // blobs and validate the prefix on retrieve. The store is otherwise stateless.
        builder.Services.TryAddScoped<IClaimCheckStore, FileStorageClaimCheckStore>();
        return builder;
    }
}
