// Pragmatic.Composition.Tests - Generator Snapshot Tests
// Snapshot tests for CompositionSourceGenerator output verification.
// Each test runs the generator on a source string and verifies the exact generated output.

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Snapshot-based tests for the CompositionSourceGenerator.
///     These tests verify the exact generated output for:
///     - Library mode: ServiceRegistration, PipelineStep registration, metadata
///     - Host mode: PragmaticHost, PragmaticEntry, Topology
///     - Decorator registration with correct ordering
///     - Module metadata emission for cross-assembly discovery
/// </summary>
public class CompositionGeneratorSnapshotTests : CompositionGeneratorTestBase
{
    #region Library Mode — ServiceRegistration Snapshots

    /// <summary>
    ///     Verifies the generated ServiceRegistration for a single service.
    /// </summary>
    [Fact]
    public async Task Library_SingleService_GeneratesServiceRegistration()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IUserService { }

            [Service]
            public class UserService : IUserService { }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated ServiceRegistration for multiple services with different lifetimes.
    /// </summary>
    [Fact]
    public async Task Library_MultipleLifetimes_GeneratesCorrectRegistrations()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IScopedService { }
            public interface ISingletonService { }
            public interface ITransientService { }

            [Service]
            public class ScopedService : IScopedService { }

            [Service(Lifetime = Lifetime.Singleton)]
            public class SingletonService : ISingletonService { }

            [Service(Lifetime = Lifetime.Transient)]
            public class TransientService : ITransientService { }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated ServiceRegistration for an open generic service.
    /// </summary>
    [Fact]
    public async Task Library_OpenGenericService_GeneratesOpenGenericRegistration()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IRepository<T> where T : class
            {
                T? GetById(int id);
            }

            [Service]
            public class Repository<T> : IRepository<T> where T : class
            {
                public T? GetById(int id) => default;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated ServiceRegistration for a self-registered service (AsSelf=true).
    /// </summary>
    [Fact]
    public async Task Library_AsSelfService_GeneratesConcreteRegistration()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            [Service(AsSelf = true)]
            public class BackgroundWorker { }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated ServiceRegistration using [Service&lt;T&gt;] generic attribute.
    /// </summary>
    [Fact]
    public async Task Library_GenericServiceAttribute_RegistersExplicitInterface()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IPaymentProvider { }
            public interface IDisposable { }

            [Service<IPaymentProvider>]
            public class StripeProvider : IPaymentProvider, IDisposable { }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated StartupStep registration.
    /// </summary>
    [Fact]
    public async Task Library_StartupStep_GeneratesStartupStepRegistration()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Abstractions;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;

            namespace TestApp;

            [StartupStep]
            public class AppStartup : IStartupStep
            {
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    #region Decorator Registration Snapshots

    /// <summary>
    ///     Verifies the generated decorator registration with a single decorator.
    /// </summary>
    [Fact]
    public async Task Decorator_SingleDecorator_GeneratesDecorateCall()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IUserService { }

            [Service]
            public class UserService : IUserService { }

            [Decorator(Order = 1)]
            public class CachingUserService : IUserService
            {
                public CachingUserService(IUserService inner) { }
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies that multiple decorators are applied in the correct order.
    /// </summary>
    [Fact]
    public async Task Decorator_MultipleDecorators_AppliedInCorrectOrder()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IUserService { }

            [Service]
            public class UserService : IUserService { }

            [Decorator(Order = 2)]
            public class LoggingUserService : IUserService
            {
                public LoggingUserService(IUserService inner) { }
            }

            [Decorator(Order = 1)]
            public class CachingUserService : IUserService
            {
                public CachingUserService(IUserService inner) { }
            }

            [Decorator(Order = 3)]
            public class RetryUserService : IUserService
            {
                public RetryUserService(IUserService inner) { }
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies decorators on different interfaces are grouped correctly.
    /// </summary>
    [Fact]
    public async Task Decorator_DifferentInterfaces_GroupedCorrectly()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IUserService { }
            public interface IOrderService { }

            [Service]
            public class UserService : IUserService { }

            [Service]
            public class OrderService : IOrderService { }

            [Decorator(Order = 1)]
            public class CachingUserService : IUserService
            {
                public CachingUserService(IUserService inner) { }
            }

            [Decorator(Order = 1)]
            public class LoggingOrderService : IOrderService
            {
                public LoggingOrderService(IOrderService inner) { }
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    #region Host Mode Snapshots

    /// <summary>
    ///     Verifies that HOST mode generates PragmaticHost and PragmaticEntry.
    /// </summary>
    [Fact]
    public async Task Host_MinimalProject_GeneratesPragmaticHostAndEntry()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Abstractions;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;

            namespace TestApp;

            public class Program { public static void Main() { } }

            [StartupStep]
            public class AppStartup : IStartupStep
            {
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }
            """;

        var result = RunHostModeGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.Contains("Host."));
        sources.Keys.Should().Contain(k => k.Contains("Entry") || k.Contains("Pragmatic.g.cs"));

        await Verify(sources);
    }

    /// <summary>
    ///     Verifies HOST mode with local services generates both service registration and host files.
    /// </summary>
    [Fact]
    public async Task Host_WithLocalServices_GeneratesServiceRegistrationAndHost()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Abstractions;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;

            namespace TestApp;

            public class Program { public static void Main() { } }

            public interface IMyService { }

            [Service]
            public class MyService : IMyService { }

            [StartupStep]
            public class AppStartup : IStartupStep
            {
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }
            """;

        var result = RunHostModeGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies HOST mode with multiple startup steps registers them all.
    /// </summary>
    [Fact]
    public async Task Host_MultipleStartupSteps_RegistersAll()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Abstractions;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;

            namespace TestApp;

            public class Program { public static void Main() { } }

            [StartupStep]
            public class DatabaseSetup : IStartupStep
            {
                public int Order => 20;
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }

            [StartupStep]
            public class AuthSetup : IStartupStep
            {
                public int Order => 10;
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }

            [StartupStep]
            public class CacheSetup : IStartupStep
            {
                public int Order => 30;
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }
            """;

        var result = RunHostModeGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies HOST mode with [NeedsStep&lt;T&gt;] on a module generates step registration.
    /// </summary>
    [Fact]
    public async Task Host_NeedsStep_RegistersDeclaredSteps()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Abstractions;
            using Pragmatic.Composition.Steps;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;

            namespace TestApp;

            public class Program { public static void Main() { } }

            [Module(Name = "TestApp")]
            [NeedsStep<RoutingStep>]
            [NeedsStep<CorsStep>]
            public partial class TestAppModule { }
            """;

        var result = RunHostModeGenerator(source);
        var sources = GetAllGeneratedSources(result);

        // Verify NeedsStep types are registered
        var hostSource = GetGeneratedSource(result, "Host.Services");
        hostSource.Should().NotBeNull();
        hostSource.Should().Contain("RoutingStep");
        hostSource.Should().Contain("CorsStep");
        hostSource.Should().Contain("[NeedsStep<T>]");

        await Verify(sources);
    }

    /// <summary>
    ///     Verifies HOST mode with both [NeedsStep&lt;T&gt;] and [StartupStep] deduplicates correctly.
    /// </summary>
    [Fact]
    public async Task Host_NeedsStepWithLocalStartup_DeduplicatesAndRegistersAll()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Abstractions;
            using Pragmatic.Composition.Steps;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;

            namespace TestApp;

            public class Program { public static void Main() { } }

            [Module(Name = "TestApp")]
            [NeedsStep<RoutingStep>]
            public partial class TestAppModule { }

            [StartupStep]
            public class AppCustomStep : IStartupStep
            {
                public int Order => 500;
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }
            """;

        var result = RunHostModeGenerator(source);
        var sources = GetAllGeneratedSources(result);

        // Verify both NeedsStep and local [StartupStep] are registered
        var hostSource = GetGeneratedSource(result, "Host.Services");
        hostSource.Should().NotBeNull();
        hostSource.Should().Contain("RoutingStep");
        hostSource.Should().Contain("AppCustomStep");

        await Verify(sources);
    }

    #endregion

    #region Module Metadata Snapshots

    /// <summary>
    ///     Verifies the generated module metadata for a simple module definition.
    /// </summary>
    [Fact]
    public async Task Metadata_SimpleModule_GeneratesModuleMetadata()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            [Module(Name = "Sales", Version = "1.0.0")]
            public class SalesModule { }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated module metadata includes description and dependencies.
    /// </summary>
    [Fact]
    public async Task Metadata_ModuleWithDependencies_GeneratesFullMetadata()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            [Module(Name = "Fulfillment", Description = "Order fulfillment module")]
            [IncludeModule<Sales.SalesModule>]
            [IncludeModule<Inventory.InventoryModule>]
            public class FulfillmentModule { }
            """;

        var result = RunGenerator(source,
            Pragmatic.Composition.Tests.Helpers.ModuleAssembly.Named("Sales"),
            Pragmatic.Composition.Tests.Helpers.ModuleAssembly.Named("Inventory"));
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated service metadata (DI) for cross-assembly discovery.
    /// </summary>
    [Fact]
    public async Task Metadata_ServiceMetadata_GeneratesDIMetadata()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IOrderService { }
            public interface IPaymentService { }

            [Service]
            public class OrderService : IOrderService { }

            [Service(Lifetime = Lifetime.Singleton)]
            public class PaymentService : IPaymentService { }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated startup metadata for startup steps.
    /// </summary>
    [Fact]
    public async Task Metadata_StartupStepMetadata_GeneratesStartupMetadata()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Abstractions;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;

            namespace TestApp;

            [StartupStep]
            public class DatabaseSetup : IStartupStep
            {
                public int Order => 10;
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }

            [StartupStep]
            public class CacheSetup : IStartupStep
            {
                public int Order => 20;
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies that a module with both services and startups generates combined metadata.
    /// </summary>
    [Fact]
    public async Task Metadata_ModuleWithServicesAndStartups_GeneratesCombinedMetadata()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Abstractions;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;

            namespace TestApp;

            [Module(Name = "Core", Version = "2.0.0")]
            public class CoreModule { }

            public interface IUserService { }

            [Service]
            public class UserService : IUserService { }

            [StartupStep]
            public class CoreStartup : IStartupStep
            {
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    #region Edge Case Snapshots

    /// <summary>
    ///     Verifies generation when services are in different namespaces.
    /// </summary>
    [Fact]
    public async Task EdgeCase_DifferentNamespaces_GeneratesCorrectPrefix()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace MyApp.Sales
            {
                public interface IOrderService { }

                [Service]
                public class OrderService : IOrderService { }
            }

            namespace MyApp.Users
            {
                public interface IUserService { }

                [Service]
                public class UserService : IUserService { }
            }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies that no COMPOSITION output is generated when there are no services or modules.
    /// </summary>
    /// <remarks>
    ///     It does not assert that nothing at all is generated: Composition is not the only feature that
    ///     can fire on this source. The Redaction feature emits its map for every assembly that can
    ///     compile one, empty included, so that an absent map means the generator did not run and
    ///     nothing else. Asserting "empty" would either break on the next always-emit artifact or force
    ///     that artifact to hide, which is the behaviour the rule exists to prevent.
    /// </remarks>
    [Fact]
    public async Task EdgeCase_NoServicesOrModules_GeneratesNoCompositionOutput()
    {
        var source = """
            namespace TestApp;

            public class RegularClass { }
            """;

        var result = RunGenerator(source);
        var sources = GetAllGeneratedSources(result);

        sources.Keys.Should().NotContain(k => k.Contains("Composition", StringComparison.Ordinal));

        await Verify(sources);
    }

    #endregion
}
