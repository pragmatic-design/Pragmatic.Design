using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     The one way to turn this package on.
/// </summary>
/// <remarks>
///     Asserted on the registration rather than on a resolved instance: building the storage needs a
///     <see cref="BlobServiceClient" />, which needs an account and a network.
/// </remarks>
public class AzureStorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAzureBlobStorage_RegistersTheFileStorage()
    {
        var services = new ServiceCollection();

        services.AddAzureBlobStorage(new AzureBlobStorageOptions());

        services.Should().Contain(d => d.ServiceType == typeof(IFileStorage));
        services.Should().Contain(d => d.ServiceType == typeof(AzureBlobStorageOptions));
    }

    /// <remarks>
    ///     The connection-string overload also brings the client, which is what makes it the one you
    ///     reach for when nothing else registered a <see cref="BlobServiceClient" />.
    /// </remarks>
    [Fact]
    public void AddAzureBlobStorage_WithAConnectionString_AlsoRegistersTheClient()
    {
        var services = new ServiceCollection();

        services.AddAzureBlobStorage(new AzureBlobStorageOptions(), "UseDevelopmentStorage=true");

        services.Should().Contain(d => d.ServiceType == typeof(BlobServiceClient));
        services.Should().Contain(d => d.ServiceType == typeof(IFileStorage));
    }

    /// <summary>
    ///     One instance, under every interface <see cref="AzureBlobFileStorage" /> implements.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Same instance and not merely resolvable: a second one would carry its own
    ///     <see cref="BlobServiceClient" /> and its own container cache, and the two would look like
    ///     one storage while talking to different accounts.
    /// </remarks>
    [Fact]
    public void AddAzureBlobStorage_ResolvesOneInstanceForEveryCapabilityItImplements()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<BlobServiceClient>(new AzureStorageSubstituteFixture().Service);

        services.AddAzureBlobStorage(new AzureBlobStorageOptions());

        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IFileStorage>();

        provider.GetRequiredService<IFileInfoProvider>().Should().BeSameAs(storage);
        provider.GetRequiredService<ISignedUrlProvider>().Should().BeSameAs(storage);
    }

    [Fact]
    public void AddAzureBlobStorage_WithoutAConnectionString_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddAzureBlobStorage(new AzureBlobStorageOptions(), "  ");

        act.Should().Throw<ArgumentException>();
    }
}
