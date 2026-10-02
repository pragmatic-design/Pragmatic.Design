using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;

namespace Pragmatic.Storage.Tests;

public class PragmaticBuilderStorageExtensionsTests
{
    private sealed class FakeBuilder : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new FakeEnvironment();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
            => Task.FromResult(new Uri("/x", UriKind.Relative));
        public Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default) => Task.FromResult(false);
        public Task DeleteAsync(Uri fileUri, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void UseStorage_WithFactory_RegistersInstance()
    {
        var builder = new FakeBuilder();
        var sentinel = new FakeFileStorage();

        builder.UseStorage(_ => sentinel);

        var resolved = builder.Services.BuildServiceProvider().GetService<IFileStorage>();
        resolved.Should().BeSameAs(sentinel);
    }

    [Fact]
    public void UseStorage_WithFactory_ReturnsSameBuilderForChaining()
    {
        var builder = new FakeBuilder();

        var result = builder.UseStorage(_ => new FakeFileStorage());

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void UseStorage_WithNullFactory_ThrowsArgumentNullException()
    {
        var builder = new FakeBuilder();

        var act = () => builder.UseStorage((Func<IServiceProvider, IFileStorage>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Fact]
    public void UseStorageGeneric_RegistersImplementationType()
    {
        var builder = new FakeBuilder();

        builder.UseStorage<FakeFileStorage>();

        var resolved = builder.Services.BuildServiceProvider().GetService<IFileStorage>();
        resolved.Should().BeOfType<FakeFileStorage>();
    }

    [Fact]
    public void UseStorageGeneric_RegistersAsSingleton()
    {
        var builder = new FakeBuilder();

        builder.UseStorage<FakeFileStorage>();

        var provider = builder.Services.BuildServiceProvider();
        provider.GetService<IFileStorage>().Should().BeSameAs(provider.GetService<IFileStorage>());
    }
}
