using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Templates;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A boundary library emits its [ServiceFactory] classes as DI
///     metadata (<see cref="ServiceFactoryMetadataTemplate" />) and the host reads them back
///     (<see cref="MetadataReader.ExtractServiceFactories" />) to register factories declared in a
///     referenced library, not only host-local ones.
/// </summary>
public class ServiceFactoryCrossAssemblyTests
{
    private static ServiceFactoryModel SampleFactory() => new()
    {
        Namespace = "App",
        FactoryClassFullName = "global::App.InfraFactories",
        Methods = ImmutableArray.Create(new FactoryMethodModel
        {
            MethodName = "CreateConnection",
            ReturnTypeFullName = "global::System.Data.IDbConnection",
            Lifetime = "Singleton",
            Parameters = ImmutableArray.Create(new FactoryParameterModel
            {
                FullTypeName = "global::Microsoft.Extensions.Configuration.IConfiguration",
                IsOptional = false
            })
        }),
        Location = null
    };

    [Fact]
    public void MetadataTemplate_EmitsServiceFactoriesAssemblyAttribute()
    {
        var source = new ServiceFactoryMetadataTemplate(ImmutableArray.Create(SampleFactory()), indent: false)
            .RenderOutput().Text;

        source.Should().Contain("PragmaticMetadata(MetadataCategory.DI");
        source.Should().Contain("\"serviceFactories\"");
        source.Should().Contain("\"class\":\"global::App.InfraFactories\"");
        source.Should().Contain("\"method\":\"CreateConnection\"");
        source.Should().Contain("\"lifetime\":\"Singleton\"");
    }

    [Fact]
    public void Reader_ParsesServiceFactoriesFromDiMetadata()
    {
        const string json =
            "{\"data\":{\"serviceFactories\":[{\"class\":\"global::App.InfraFactories\"," +
            "\"methods\":[{\"method\":\"CreateConnection\",\"returnType\":\"global::System.Data.IDbConnection\"," +
            "\"lifetime\":\"Singleton\",\"parameters\":[{\"type\":\"global::Microsoft.Extensions.Configuration.IConfiguration\",\"optional\":false}]}]}]}}";

        var assemblies = ImmutableArray.Create(new AssemblyMetadataModel
        {
            AssemblyName = "App.Billing",
            Entries = ImmutableArray.Create(new MetadataEntry
            {
                Category = MetadataCategoryIds.DI,
                SchemaVersion = "1.0.0",
                RegistrationMethod = string.Empty,
                JsonData = json
            })
        });

        var factories = MetadataReader.ExtractServiceFactories(assemblies);

        factories.Should().HaveCount(1);
        var f = factories[0];
        f.FactoryClassFullName.Should().Be("global::App.InfraFactories");
        f.Methods.Should().HaveCount(1);
        var m = f.Methods[0];
        m.MethodName.Should().Be("CreateConnection");
        m.ReturnTypeFullName.Should().Be("global::System.Data.IDbConnection");
        m.Lifetime.Should().Be("Singleton");
        m.Parameters.Should().ContainSingle().Which.FullTypeName
            .Should().Be("global::Microsoft.Extensions.Configuration.IConfiguration");
    }

    [Fact]
    public void HostRegistration_IncludesDiscoveredFactory()
    {
        // The host template renders discovered (cross-assembly) factories the same way as host-local ones.
        var source = new ServiceFactoryRegistrationTemplate(ImmutableArray.Create(SampleFactory()), "MyHost")
            .RenderOutput().Text;

        source.Should().Contain(
            "services.AddSingleton<global::System.Data.IDbConnection>(sp => sp.GetRequiredService<global::App.InfraFactories>().CreateConnection(sp.GetRequiredService<global::Microsoft.Extensions.Configuration.IConfiguration>()));");
    }
}
