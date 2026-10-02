using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     What <c>AddLocalDiskStorage</c> registers is what <see cref="LocalDiskFileStorage" />
///     implements.
/// </summary>
/// <remarks>
///     <para>
///         The type is <c>IFileStorage, IFileInfoProvider</c>, and <c>AddInMemoryStorage</c> registers
///         both. Registering <see cref="IFileStorage" /> alone here would let an application injecting
///         <see cref="IFileInfoProvider" /> resolve it under its tests, which use the in-memory store,
///         and fail to build in production, which uses the disk. Same code, same seam, and the
///         environment that works would be the one nobody ships.
///     </para>
///     <para>
///         ⚠️ One instance under two registrations, not two instances. A second one would read the
///         same directory here and would be a different bucket, session or connection on every other
///         provider.
///     </para>
/// </remarks>
public sealed class TheLocalDiskRegistrationCarriesItsCapabilitiesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-reg-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    /// <remarks>
    ///     The logger by hand rather than <c>AddLogging()</c>: this project depends on the logging
    ///     abstractions and not on the implementation, and what is under test is the storage
    ///     registration.
    /// </remarks>
    private static ServiceProvider Application(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<LocalDiskFileStorage>>(NullLogger<LocalDiskFileStorage>.Instance);
        register(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddLocalDiskStorage_ResolvesTheSameInstanceForBothInterfaces()
    {
        using var application = Application(s => s.AddLocalDiskStorage(_root));

        var storage = application.GetRequiredService<IFileStorage>();
        var info = application.GetRequiredService<IFileInfoProvider>();

        info.Should().BeSameAs(storage,
            "metadata queries have to see the files written through the storage, which is only true of one instance");
    }

    /// <summary>
    ///     ⚠️ The size-limited overload too.
    /// </summary>
    /// <remarks>
    ///     Two overloads register the same type, and an application that sets an upload limit takes the
    ///     other one. Covering the first alone would leave exactly the same failure behind, reachable by
    ///     the applications careful enough to cap their uploads.
    /// </remarks>
    [Fact]
    public void AddLocalDiskStorage_WithASizeLimit_ResolvesTheSameInstanceForBothInterfaces()
    {
        using var application = Application(s => s.AddLocalDiskStorage(_root, maxFileSizeBytes: 1024));

        var storage = application.GetRequiredService<IFileStorage>();
        var info = application.GetRequiredService<IFileInfoProvider>();

        info.Should().BeSameAs(storage);
    }

    /// <summary>The registration is the seam; this is it doing its job end to end.</summary>
    [Fact]
    public async Task TheResolvedInfoProvider_SeesWhatTheResolvedStorageWrote()
    {
        using var application = Application(s => s.AddLocalDiskStorage(_root));

        var uri = await application.GetRequiredService<IFileStorage>()
            .SaveAsync(new MemoryStream([1, 2, 3, 4]), "data.bin", "docs");

        var info = await application.GetRequiredService<IFileInfoProvider>().GetInfoAsync(uri);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(4);
    }
}
