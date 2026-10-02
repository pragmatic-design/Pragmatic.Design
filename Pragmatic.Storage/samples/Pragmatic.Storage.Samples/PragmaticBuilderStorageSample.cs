using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Composition;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Samples;

/// <summary>
/// Demonstrates the <see cref="IPragmaticBuilder"/> storage extensions:
/// <c>UseStorage&lt;TStorage&gt;()</c> (type-based) and <c>UseStorage(factory)</c> (factory-based).
/// In a real app the builder comes from <c>PragmaticApp.RunAsync</c>; here we use a minimal
/// stand-in so the sample is self-contained.
/// </summary>
public static class PragmaticBuilderStorageSample
{
    public static void Run()
    {
        Console.WriteLine("--- IPragmaticBuilder.UseStorage ---");

        var demoRoot = SampleSupport.CreateTempRoot();
        try
        {
            // A) Type-based: UseStorage<TStorage>() registers TStorage as IFileStorage and lets
            //    the container activate it. The type must be DI-constructible; InMemoryFileStorage
            //    (parameterless ctor) qualifies, whereas LocalDiskFileStorage needs a base path.
            var builderA = new SampleBuilder();
            builderA.UseStorage<InMemoryFileStorage>();
            using (var providerA = builderA.Services.BuildServiceProvider())
            {
                var storageA = providerA.GetRequiredService<IFileStorage>();
                Console.WriteLine($"UseStorage<InMemoryFileStorage>() -> {storageA.GetType().Name}");
            }

            // B) Factory-based: UseStorage(factory) builds the implementation from the container.
            var builderB = new SampleBuilder();
            builderB.Services.AddLogging();
            builderB.UseStorage(sp => new LocalDiskFileStorage(
                demoRoot, sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
            using (var providerB = builderB.Services.BuildServiceProvider())
            {
                var storageB = providerB.GetRequiredService<IFileStorage>();
                Console.WriteLine($"UseStorage(factory) -> {storageB.GetType().Name}");
            }
        }
        finally
        {
            SampleSupport.Cleanup(demoRoot);
        }

        Console.WriteLine();
    }

    /// <summary>Minimal <see cref="IPragmaticBuilder"/> for standalone demonstration.</summary>
    private sealed class SampleBuilder : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new SampleEnvironment();

        private sealed class SampleEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "Pragmatic.Storage.Samples";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
                new Microsoft.Extensions.FileProviders.NullFileProvider();
        }
    }
}
