using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     Covers <see cref="SftpStorageServiceCollectionExtensions.AddSftpStorage" />: registers the
///     options plus an <see cref="IFileStorage" /> singleton that also exposes the
///     <see cref="IFileInfoProvider" /> capability.
/// </summary>
public sealed class SftpStorageServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider(SftpStorageOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<SftpFileStorage>>(NullLogger<SftpFileStorage>.Instance);
        services.AddSftpStorage(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSftpStorage_RegistersFileStorageSingleton()
    {
        using var provider = BuildProvider(new SftpStorageOptions
        {
            Host = "host",
            Username = "user",
            Password = "pass",
        });

        var storage = provider.GetRequiredService<IFileStorage>();

        storage.Should().BeOfType<SftpFileStorage>();
        storage.Should().BeAssignableTo<IFileInfoProvider>();
    }

    /// <summary>
    ///     ⚠️ The capability is <em>registered</em>, not merely implemented.
    /// </summary>
    /// <remarks>
    ///     The case above asserts that the resolved type is assignable to
    ///     <see cref="IFileInfoProvider" />, which the class declaration alone makes true — and it was
    ///     true while nothing registered the interface, so an application injecting it failed to build.
    ///     Asking the container is the difference, and the same instance is what makes the metadata
    ///     answers come from the same connection.
    /// </remarks>
    [Fact]
    public void AddSftpStorage_ResolvesOneInstanceForEveryCapabilityItImplements()
    {
        using var provider = BuildProvider(new SftpStorageOptions
        {
            Host = "host",
            Username = "user",
            Password = "pass",
        });

        var storage = provider.GetRequiredService<IFileStorage>();

        provider.GetRequiredService<IFileInfoProvider>().Should().BeSameAs(storage);
    }

    [Fact]
    public void AddSftpStorage_RegistersOptions()
    {
        var options = new SftpStorageOptions { Host = "host", Username = "user", Password = "pass" };
        using var provider = BuildProvider(options);

        provider.GetRequiredService<SftpStorageOptions>().Should().BeSameAs(options);
    }

    [Fact]
    public void AddSftpStorage_NullOptions_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSftpStorage(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
