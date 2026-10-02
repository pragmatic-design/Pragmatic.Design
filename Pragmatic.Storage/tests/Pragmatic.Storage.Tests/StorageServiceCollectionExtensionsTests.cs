using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

public class StorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLocalDiskStorage_RegistersIFileStorage()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));

        services.AddLocalDiskStorage("/tmp/test");

        var provider = services.BuildServiceProvider();
        var storage = provider.GetService<IFileStorage>();
        storage.Should().NotBeNull();
        storage.Should().BeOfType<LocalDiskFileStorage>();
    }

    [Fact]
    public void AddLocalDiskStorage_ReturnsSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddLocalDiskStorage("/tmp/test");

        result.Should().BeSameAs(services);
    }

    [Fact]
    public async Task AddLocalDiskStorage_WithMaxFileSize_RejectsOversizedUpload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-di-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));

        services.AddLocalDiskStorage(tempDir, maxFileSizeBytes: 16);

        var storage = services.BuildServiceProvider().GetRequiredService<IFileStorage>();
        using var stream = new MemoryStream(new byte[32]);
        try
        {
            var act = () => storage.SaveAsync(stream, "big.bin", "docs");
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddLocalDiskStorage_WithMaxFileSize_RegistersIFileStorage()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));

        services.AddLocalDiskStorage("/tmp/test", maxFileSizeBytes: 1024);

        var storage = services.BuildServiceProvider().GetService<IFileStorage>();
        storage.Should().BeOfType<LocalDiskFileStorage>();
    }

    [Fact]
    public void AddFileStorage_RegistersCustomImplementation()
    {
        var services = new ServiceCollection();

        services.AddFileStorage<FakeFileStorage>();

        var provider = services.BuildServiceProvider();
        var storage = provider.GetService<IFileStorage>();
        storage.Should().NotBeNull();
        storage.Should().BeOfType<FakeFileStorage>();
    }

    [Fact]
    public void AddFileStorage_RegistersAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddFileStorage<FakeFileStorage>();

        var provider = services.BuildServiceProvider();
        provider.GetService<IFileStorage>().Should().BeSameAs(provider.GetService<IFileStorage>());
    }

    [Fact]
    public void AddLocalDiskStorage_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));

        services.AddLocalDiskStorage("/tmp/test");

        var provider = services.BuildServiceProvider();
        provider.GetService<IFileStorage>().Should().BeSameAs(provider.GetService<IFileStorage>());
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public Task<Uri> SaveAsync(Stream content, string fileName, string container,
            CancellationToken ct = default)
            => Task.FromResult(new Uri($"/files/{container}/{fileName}", UriKind.Relative));

        public Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
            => Task.FromResult<Stream?>(null);

        public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
