// Pragmatic.Composition.Tests - Cross-assembly [PragmaticMetadata] emission tests.
// Covers cross-assembly discovery aggregation: the [PragmaticMetadata] a host reads.
// These verify the PRODUCER side (the metadata attributes a library assembly emits) that the host
// generator consumes for zero-reflection aggregation. Library mode is deterministic in this harness,
// mirroring the existing ModuleGeneratorTests / CompositionGeneratorSnapshotTests setup.
//
// Intentionally NOT asserted here (require a fully-wired HOST compilation with referenced module DLLs
// carrying [PragmaticMetadata] discovered actions + an appsettings AdditionalText — only reproducible
// in the Showcase integration host, not this unit harness):
//   - [RemoteBoundary<T>] host diagnostics PRAG1685/1686/1687 + HttpInvoker generation
//   - [UsePackage<T>] host fusion + PRAG1050 duplicate diagnostic
//   - [RequiresConfig] generated ValidateConfiguration body

using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Composition.Tests.Helpers;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Library-mode generator tests for the cross-assembly [PragmaticMetadata] attributes that drive
///     host-side aggregation (DI, Startup, Module categories).
/// </summary>
public class CompositionFeatureGeneratorTests
{
    [Fact]
    public void Library_Service_EmitsPragmaticMetadataDI()
    {
        // A library assembly emits [assembly: PragmaticMetadata(MetadataCategory.DI, ...)] so a host
        // can discover and wire its services across the assembly boundary without reflection.
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IOrderService { }

            [Service]
            public class OrderService : IOrderService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        output.Should().ContainKey("_Metadata.DI.g.cs");
        var diMetadata = output["_Metadata.DI.g.cs"];
        diMetadata.Should().Contain("PragmaticMetadata(MetadataCategory.DI");
        // JSON whitespace varies with build configuration (Debug=indented / Release=compact),
        // so assert the property tokens and values independently rather than a spaced literal.
        diMetadata.Should().Contain("\"interface\"");
        diMetadata.Should().Contain("global::TestApp.IOrderService");
        diMetadata.Should().Contain("\"implementation\"");
        diMetadata.Should().Contain("global::TestApp.OrderService");
    }

    [Fact]
    public void Library_KeyedService_EmitsKeyInDIMetadata()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IProvider { }

            [Service(Key = "primary")]
            public class PrimaryProvider : IProvider { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        output.Should().ContainKey("_Metadata.DI.g.cs");
        // The key flows into cross-assembly DI metadata so the host can emit keyed registration.
        // JSON whitespace varies with build config — assert the property token and value separately.
        var diMetadata = output["_Metadata.DI.g.cs"];
        diMetadata.Should().Contain("\"key\"");
        diMetadata.Should().Contain("primary");
    }

    [Fact]
    public void Library_StartupStep_EmitsPragmaticMetadataStartup()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Abstractions;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;

            namespace TestApp;

            [StartupStep]
            public class CoreStartup : IStartupStep
            {
                public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        // The startup metadata attribute is emitted for host-side cross-assembly aggregation.
        output.Values.Should().Contain(v => v.Contains("PragmaticMetadata(MetadataCategory.Startup"));
    }

    [Fact]
    public void Library_Module_EmitsPragmaticMetadataModuleWithDependencies()
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

        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        output.Should().ContainKey("_Metadata.Module.g.cs");
        var metadata = output["_Metadata.Module.g.cs"];
        metadata.Should().Contain("PragmaticMetadata(MetadataCategory.Module");
        metadata.Should().Contain("\"dependsOn\"");
        metadata.Should().Contain("\"Sales\"");
        metadata.Should().Contain("\"Inventory\"");
    }
}
