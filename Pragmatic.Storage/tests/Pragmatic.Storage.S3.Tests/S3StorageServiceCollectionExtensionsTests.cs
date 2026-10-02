using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Tests.Generated;
using Xunit;

namespace Pragmatic.Storage.S3.Tests;

/// <summary>
///     The one way to turn this package on.
/// </summary>
/// <remarks>
///     Asserted on the registration rather than on a resolved instance: building the storage needs an
///     <c>IAmazonS3</c>, which needs credentials and a network. What has to hold here is that asking
///     for S3 leaves an <c>IFileStorage</c> for the application to find.
/// </remarks>
public class S3StorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddS3Storage_RegistersTheFileStorage()
    {
        var services = new ServiceCollection();

        services.AddS3Storage(new S3StorageOptions { BucketName = "myapp-files" });

        services.Should().Contain(d => d.ServiceType == typeof(IFileStorage));
        services.Should().Contain(d => d.ServiceType == typeof(S3StorageOptions),
            "the options travel with it, so the storage can read the bucket it was given");
    }

    /// <summary>
    ///     One instance, under every interface <see cref="S3FileStorage" /> implements.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Registering <see cref="IFileStorage" /> alone would make an application injecting
    ///         <see cref="IFileInfoProvider" /> — a size, a content type, without downloading the object
    ///         — fail to build, while the same application under the in-memory store resolves it and
    ///         runs green.
    ///     </para>
    ///     <para>
    ///         ⚠️ Same instance and not merely resolvable. A second <see cref="S3FileStorage" /> would
    ///         carry its own client and its own bucket configuration, and the two would answer about
    ///         different objects while looking like one storage.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AddS3Storage_ResolvesOneInstanceForEveryCapabilityItImplements()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAmazonS3>(new AmazonS3Mock());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddS3Storage(new S3StorageOptions { BucketName = "myapp-files" });

        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IFileStorage>();

        storage.Should().BeOfType<S3FileStorage>();
        provider.GetRequiredService<IFileInfoProvider>().Should().BeSameAs(storage);
        provider.GetRequiredService<ISignedUrlProvider>().Should().BeSameAs(storage);
    }

    [Fact]
    public void AddS3Storage_WithoutOptions_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddS3Storage(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
