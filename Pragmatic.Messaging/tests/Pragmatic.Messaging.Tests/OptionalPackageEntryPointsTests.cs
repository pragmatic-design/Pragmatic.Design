using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Batch;
using Pragmatic.Messaging.EFCore;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Messaging.Tests;

/// <summary>
///     The one way to turn each optional messaging package on.
/// </summary>
/// <remarks>
///     <c>EnableBatchProcessing</c> and <c>EnableEfCorePersistence</c> are the only entry points their
///     packages have, and nothing called them, no test ran them and no page named them.
/// </remarks>
public class OptionalPackageEntryPointsTests
{
    /// <remarks>
    ///     The middleware is the half that matters: without it a batch stays active forever when a
    ///     handler throws, because nothing reports the item's outcome.
    /// </remarks>
    [Fact]
    public void EnableBatchProcessing_RegistersTheStoreAndTheProgressMiddleware()
    {
        var services = new ServiceCollection();
        services.AddPragmaticMessaging(m => m.EnableBatchProcessing());

        services.Should().Contain(d => d.ServiceType == typeof(IBatchProgressStore));
        services.Should().Contain(d => d.ServiceType == typeof(IMessageMiddleware)
                                       && d.ImplementationType == typeof(BatchProgressMiddleware));
    }

    /// <remarks>
    ///     Replacement, not addition: leaving the in-memory idempotency store registered alongside
    ///     would mean a redelivery deduplicated against a table that forgets on restart.
    /// </remarks>
    [Fact]
    public void EnableEfCorePersistence_ReplacesTheIdempotencyStore()
    {
        var services = new ServiceCollection();

        services.AddPragmaticMessaging(m => m.EnableEfCorePersistence());

        services.Count(d => d.ServiceType == typeof(IIdempotencyStore)).Should().Be(1);
        services.Should().Contain(d => d.ServiceType == typeof(IIdempotencyStore)
                                       && d.ImplementationType == typeof(EfCoreIdempotencyStore));
    }
}
