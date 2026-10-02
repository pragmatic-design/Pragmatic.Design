using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     Covers <see cref="FtpStorageServiceCollectionExtensions.AddFtpStorage" />: it registers the
///     options and an <see cref="IFileStorage" /> singleton backed by <see cref="FtpFileStorage" />.
/// </summary>
public sealed class FtpStorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFtpStorage_RegistersFileStorageSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddFtpStorage(new FtpStorageOptions { Host = "ftp.example.com", Username = "user" });

        using var provider = services.BuildServiceProvider();
        var storage = provider.GetService<IFileStorage>();

        storage.Should().BeOfType<FtpFileStorage>();
        provider.GetService<FtpStorageOptions>().Should().NotBeNull();
    }

    /// <summary>
    ///     One instance, under every interface <see cref="FtpFileStorage" /> implements.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Same instance and not merely resolvable: a second one would open its own connections to
    ///     the same host, and the two would look like one storage while holding two sessions.
    /// </remarks>
    [Fact]
    public void AddFtpStorage_ResolvesOneInstanceForEveryCapabilityItImplements()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddFtpStorage(new FtpStorageOptions { Host = "ftp.example.com", Username = "user" });

        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IFileStorage>();

        provider.GetRequiredService<IFileInfoProvider>().Should().BeSameAs(storage);
    }

    [Fact]
    public void AddFtpStorage_NullOptions_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddFtpStorage(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
