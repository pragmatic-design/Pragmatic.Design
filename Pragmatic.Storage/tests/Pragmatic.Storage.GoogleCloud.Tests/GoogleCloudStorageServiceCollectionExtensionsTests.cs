using Google.Cloud.Storage.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>
///     Covers <see cref="GoogleCloudStorageServiceCollectionExtensions.AddGoogleCloudStorage" />:
///     the options singleton is registered and <see cref="IFileStorage" /> resolves to a
///     <see cref="GoogleCloudFileStorage" /> built from the container-registered
///     <see cref="StorageClient" />.
/// </summary>
public sealed class GoogleCloudStorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGoogleCloudStorage_ResolvesFileStorageFromContainerStorageClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<StorageClient>(new StorageClientMock());
        var options = new GoogleCloudStorageOptions { BucketName = "bkt" };

        services.AddGoogleCloudStorage(options);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFileStorage>().Should().BeOfType<GoogleCloudFileStorage>();
        provider.GetRequiredService<GoogleCloudStorageOptions>().Should().BeSameAs(options);
    }

    /// <summary>
    ///     One instance, under every interface <see cref="GoogleCloudFileStorage" /> implements.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Same instance and not merely resolvable: a second one would carry its own client and its
    ///     own bucket, and the two would answer about different objects while looking like one storage.
    /// </remarks>
    [Fact]
    public void AddGoogleCloudStorage_ResolvesOneInstanceForEveryCapabilityItImplements()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<StorageClient>(new StorageClientMock());

        services.AddGoogleCloudStorage(new GoogleCloudStorageOptions { BucketName = "bkt" });

        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IFileStorage>();

        provider.GetRequiredService<IFileInfoProvider>().Should().BeSameAs(storage);
        provider.GetRequiredService<ISignedUrlProvider>().Should().BeSameAs(storage);
    }

    [Fact]
    public void AddGoogleCloudStorage_NullOptions_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddGoogleCloudStorage(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
