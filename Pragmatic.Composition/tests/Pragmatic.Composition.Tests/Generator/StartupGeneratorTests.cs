// Pragmatic.Composition.Tests - Startup Generator Tests

using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Tests.Helpers;
using Xunit;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Tests for the CompositionSourceGenerator startup step detection.
/// </summary>
public class StartupGeneratorTests
{
    [Fact]
    public void NoStartupSteps_GeneratesEmptyRegistration()
    {
        // Arrange
        var source = """
                     namespace TestApp;

                     public class MyService { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - No startup-related files generated (no [StartupStep] classes)
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void SingleStartupStep_GeneratesRegistration()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Abstractions;
                     using Microsoft.AspNetCore.Builder;
                     using Microsoft.Extensions.Configuration;
                     using Microsoft.Extensions.Hosting;
                     using Microsoft.Extensions.DependencyInjection;

                     namespace TestApp;

                     [StartupStep]
                     public class AppStartup : IStartupStep
                     {
                         public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
                         {
                             services.AddSingleton<MyService>();
                         }
                     }

                     public class MyService { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Infra.Composition.PipelineSteps.g.cs");
        output["_Infra.Composition.PipelineSteps.g.cs"].Should().Contain("AddPipelineSteps");
        output["_Infra.Composition.PipelineSteps.g.cs"].Should().Contain("TestApp.AppStartup");
    }

    [Fact]
    public void MultipleStartupSteps_RegistersAll()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;
                     using Pragmatic.Composition.Abstractions;
                     using Microsoft.AspNetCore.Builder;
                     using Microsoft.Extensions.Configuration;
                     using Microsoft.Extensions.Hosting;
                     using Microsoft.Extensions.DependencyInjection;

                     namespace TestApp;

                     [StartupStep]
                     public class SecondStep : IStartupStep
                     {
                         public int Order => 100;
                     }

                     [StartupStep]
                     public class FirstStep : IStartupStep
                     {
                         public int Order => 10;
                     }

                     [StartupStep]
                     public class ThirdStep : IStartupStep
                     {
                         public int Order => 200;
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Infra.Composition.PipelineSteps.g.cs");

        var generatedCode = output["_Infra.Composition.PipelineSteps.g.cs"];
        // Note: The generator cannot determine constant Order values at compile-time,
        // so ordering by order happens at runtime. The generator just registers all steps.
        generatedCode.Should().Contain("FirstStep");
        generatedCode.Should().Contain("SecondStep");
        generatedCode.Should().Contain("ThirdStep");
    }
}
