// Pragmatic.Composition.Tests - Module Generator Tests

using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Composition.Tests.Helpers;
using Xunit;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Tests for the CompositionSourceGenerator module detection.
/// </summary>
public class ModuleGeneratorTests
{
    [Fact]
    public void Module_SimpleDefinition_GeneratesMetadata()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "TestModule", Version = "1.0.0")]
                     public class TestModule { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Metadata.Module.g.cs");
        var generatedCode = output["_Metadata.Module.g.cs"];
        generatedCode.Should().Contain("MetadataCategory.Module");
        generatedCode.Should().Contain("\"name\": \"TestModule\"");
        generatedCode.Should().Contain("\"version\": \"1.0.0\"");
    }

    [Fact]
    public void Module_WithDescription_IncludesDescription()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "TestModule", Description = "A test module for validation")]
                     public class TestModule { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Metadata.Module.g.cs");
        output["_Metadata.Module.g.cs"].Should()
            .Contain("\"description\": \"A test module for validation\"");
    }

    /// <summary>
    ///     A module names its dependencies by type, once.
    /// </summary>
    /// <remarks>
    ///     <c>[Module(DependsOn = new[] { "Core" })]</c> would be a second, string-typed spelling of
    ///     <c>[IncludeModule&lt;T&gt;]</c>: against "attributes are generic" (a type goes in as a generic
    ///     argument) and "no [Obsolete] before 1.0" (the old form goes with its callers). A name nobody
    ///     can check at compile time is a typo PRAG1601 can only catch after the fact.
    /// </remarks>
    [Fact]
    public void TheModuleAttribute_HasNoStringDependsOn()
        => typeof(Pragmatic.Composition.Attributes.ModuleAttribute).GetProperty("DependsOn")
            .Should().BeNull("a dependency is declared with [IncludeModule<TModule>], which the compiler checks");

    [Fact]
    public void Module_WithDependencies_GeneratesDependsOnArray()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "TestModule")]
                     [IncludeModule<Core.CoreModule>]
                     [IncludeModule<Shared.SharedModule>]
                     public class TestModule { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(
            source, ModuleAssembly.Named("Core"), ModuleAssembly.Named("Shared"));

        // Assert
        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Metadata.Module.g.cs");
        var generatedCode = output["_Metadata.Module.g.cs"];
        generatedCode.Should().Contain("\"dependsOn\"");
        generatedCode.Should().Contain("\"Core\"");
        generatedCode.Should().Contain("\"Shared\"");
    }

    [Fact]
    public void Module_WithServices_IncludesRegistrationMethod()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Module(Name = "TestModule")]
                     public class TestModule { }

                     public interface IMyService { }

                     [Service]
                     public class MyService : IMyService { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        // Filter out keyed services warning (PRAG1646)
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        errors.Should().BeEmpty();

        output.Should().ContainKey("_Metadata.Module.g.cs");
        var generatedCode = output["_Metadata.Module.g.cs"];
        generatedCode.Should().Contain("\"registrations\"");
        generatedCode.Should().Contain("\"di\"");
    }

    [Fact]
    public void Module_WithStartups_IncludesStartupRegistrationMethod()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Abstractions;
                     using Microsoft.Extensions.DependencyInjection;
                     using Microsoft.Extensions.Configuration;
                     using Microsoft.Extensions.Hosting;

                     namespace TestApp;

                     [Module(Name = "TestModule")]
                     public class TestModule { }

                     [StartupStep]
                     public class TestStartup : IStartupStep
                     {
                         public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Metadata.Module.g.cs");
        var generatedCode = output["_Metadata.Module.g.cs"];
        generatedCode.Should().Contain("\"registrations\"");
        generatedCode.Should().Contain("\"startup\"");
    }
}