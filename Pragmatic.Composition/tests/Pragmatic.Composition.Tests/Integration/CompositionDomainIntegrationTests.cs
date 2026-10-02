// Pragmatic.Composition.Tests - Composition Domain Integration Tests
// Tests for integration between Composition module system and Domain entities/actions.

using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Composition.Tests.Helpers;
using Xunit;

namespace Pragmatic.Composition.Tests.Integration;

/// <summary>
///     Integration tests verifying that Domain entities and actions work correctly
///     with the Composition module metadata system.
/// </summary>
public class CompositionDomainIntegrationTests
{
    #region Module with Domain Actions

    /// <summary>
    ///     Verifies that a module with Domain actions generates correct metadata.
    /// </summary>
    [Fact]
    public void Module_WithDomainAction_GeneratesCorrectMetadata()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "Sales")]
                     public class SalesModule { }

                     // Simulating Domain action metadata
                     public interface IDomainAction<TResult> { }

                     public class PlaceOrder : IDomainAction<Guid>
                     {
                         public Guid CustomerId { get; init; }
                     }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Metadata.Module.g.cs");
        output["_Metadata.Module.g.cs"].Should().Contain("\"name\": \"Sales\"");
    }

    #endregion

    #region Multiple Modules with Dependencies

    /// <summary>
    ///     Verifies that modules with dependencies generate correct dependency metadata.
    /// </summary>
    [Fact]
    public void Module_WithDependencies_GenerateCorrectDependsOn()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "Fulfillment")]
                     [IncludeModule<Sales.SalesModule>]
                     [IncludeModule<Inventory.InventoryModule>]
                     public class FulfillmentModule { }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(
            source, ModuleAssembly.Named("Sales"), ModuleAssembly.Named("Inventory"));

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        errors.Should().BeEmpty();

        // Verify Fulfillment depends on both Sales and Inventory
        output.Should().ContainKey("_Metadata.Module.g.cs");
        var fulfillmentModule = output["_Metadata.Module.g.cs"];
        fulfillmentModule.Should().Contain("\"dependsOn\"");
        fulfillmentModule.Should().Contain("\"Sales\"");
        fulfillmentModule.Should().Contain("\"Inventory\"");
    }

    #endregion

    #region Module with Services and Startups

    /// <summary>
    ///     Verifies that a module with services generates combined DI registration.
    /// </summary>
    [Fact]
    public void Module_WithServicesAndStartups_GeneratesCombinedRegistration()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Abstractions;
                     using Microsoft.Extensions.DependencyInjection;
                     using Microsoft.Extensions.Configuration;
                     using Microsoft.Extensions.Hosting;

                     namespace TestApp;

                     [Module(Name = "Sales")]
                     public class SalesModule { }

                     public interface IOrderService { }

                     [Service]
                     public class OrderService : IOrderService { }

                     [StartupStep]
                     public class SalesStartup : IStartupStep
                     {
                         public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env)
                         {
                             // Configure sales-specific services
                         }
                     }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        errors.Should().BeEmpty();

        // Should generate service registration
        output.Should().ContainKey("_Infra.DI.ServiceRegistration.g.cs");
        var serviceReg = output["_Infra.DI.ServiceRegistration.g.cs"];
        serviceReg.Should().Contain("AddPragmaticServices");
        serviceReg.Should().Contain("OrderService");

        // Should generate module metadata with registrations
        output.Should().ContainKey("_Metadata.Module.g.cs");
        var moduleMetadata = output["_Metadata.Module.g.cs"];
        moduleMetadata.Should().Contain("\"registrations\"");
        moduleMetadata.Should().Contain("\"di\"");
        moduleMetadata.Should().Contain("\"startup\"");
    }

    #endregion

    #region Services with Multiple Lifetimes

    /// <summary>
    ///     Verifies that services with different lifetimes are correctly generated.
    /// </summary>
    [Fact]
    public void Services_WithMultipleLifetimes_GenerateCorrectRegistration()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "TestModule")]
                     public class TestModule { }

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

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        errors.Should().BeEmpty();

        var serviceReg = output["_Infra.DI.ServiceRegistration.g.cs"];
        serviceReg.Should().Contain("AddScoped<global::TestApp.IScopedService, global::TestApp.ScopedService>");
        serviceReg.Should().Contain("AddSingleton<global::TestApp.ISingletonService, global::TestApp.SingletonService>");
        serviceReg.Should().Contain("AddTransient<global::TestApp.ITransientService, global::TestApp.TransientService>");
    }

    #endregion

    #region Module with StartupStep

    /// <summary>
    ///     Verifies that modules with StartupStep generate correct registration metadata.
    /// </summary>
    [Fact]
    public void Module_WithStartupStep_GeneratesStartupRegistration()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Abstractions;
                     using Microsoft.Extensions.DependencyInjection;
                     using Microsoft.Extensions.Configuration;
                     using Microsoft.Extensions.Hosting;

                     namespace TestApp;

                     [Module(Name = "Infrastructure")]
                     public class InfrastructureModule { }

                     [StartupStep]
                     public class DatabaseStartupStep : IStartupStep
                     {
                         public int Order => 10;
                         public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env)
                         {
                             // Configure database services
                         }
                     }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().BeEmpty();

        // Should generate module metadata with startup step
        output.Should().ContainKey("_Metadata.Module.g.cs");
        var moduleMetadata = output["_Metadata.Module.g.cs"];
        moduleMetadata.Should().Contain("\"registrations\"");
        moduleMetadata.Should().Contain("\"startup\"");  // Pipeline steps are registered as startup
    }

    #endregion

    #region Decorator Chain

    /// <summary>
    ///     Verifies that decorators are applied in correct order.
    /// </summary>
    [Fact]
    public void Decorators_WithMultipleOrders_ApplyInCorrectSequence()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "Core")]
                     public class CoreModule { }

                     public interface IUserService { }

                     [Service]
                     public class UserService : IUserService { }

                     [Decorator(Order = 1)]
                     public class CachingUserService : IUserService
                     {
                         private readonly IUserService _inner;
                         public CachingUserService(IUserService inner) => _inner = inner;
                     }

                     [Decorator(Order = 2)]
                     public class LoggingUserService : IUserService
                     {
                         private readonly IUserService _inner;
                         public LoggingUserService(IUserService inner) => _inner = inner;
                     }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        errors.Should().BeEmpty();

        // Core should have base service and decorators
        output.Should().ContainKey("_Infra.DI.ServiceRegistration.g.cs");
        var serviceReg = output["_Infra.DI.ServiceRegistration.g.cs"];
        serviceReg.Should().Contain("UserService");
        serviceReg.Should().Contain("CachingUserService");
        serviceReg.Should().Contain("LoggingUserService");

        // Decorators should be in order
        var cachingIndex = serviceReg.IndexOf("CachingUserService", StringComparison.Ordinal);
        var loggingIndex = serviceReg.IndexOf("LoggingUserService", StringComparison.Ordinal);
        cachingIndex.Should().BeLessThan(loggingIndex, "Decorator with Order=1 should appear before Order=2");
    }

    #endregion

    #region Module Metadata Schema Version

    /// <summary>
    ///     Verifies that generated metadata includes schema version for HOST validation.
    /// </summary>
    [Fact]
    public void Module_GeneratesMetadata_WithSchemaVersion()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "Sales", Version = "2.0.0")]
                     public class SalesModule { }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Metadata.Module.g.cs");

        var metadata = output["_Metadata.Module.g.cs"];
        // Schema version should be present for HOST validation
        metadata.Should().Contain("1.0.0");  // SchemaVersion
        // Module version should be present
        metadata.Should().Contain("\"version\": \"2.0.0\"");
    }

    #endregion

    #region Open Generic Services

    /// <summary>
    ///     Verifies that open generic services are correctly registered.
    /// </summary>
    [Fact]
    public void OpenGenericService_GeneratesCorrectRegistration()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "Infrastructure")]
                     public class InfrastructureModule { }

                     public interface IRepository<T> where T : class { }

                     [Service]
                     public class Repository<T> : IRepository<T> where T : class { }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        errors.Should().BeEmpty();

        var serviceReg = output["_Infra.DI.ServiceRegistration.g.cs"];
        serviceReg.Should().Contain("AddScoped(typeof(global::TestApp.IRepository<>), typeof(global::TestApp.Repository<>))");
    }

    #endregion
}
