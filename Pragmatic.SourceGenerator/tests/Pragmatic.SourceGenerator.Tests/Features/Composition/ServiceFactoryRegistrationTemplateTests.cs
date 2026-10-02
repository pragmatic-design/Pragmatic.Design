using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     [ServiceFactory]/[Factory] must generate real DI registration — the factory class as a
///     singleton and each [Factory] method registering its return type via a factory that calls it
///     (parameters resolved from DI).
/// </summary>
public class ServiceFactoryRegistrationTemplateTests
{
    [Fact]
    public void RenderOutput_RegistersFactoryClassSingleton_AndEachMethod()
    {
        var model = new ServiceFactoryModel
        {
            Namespace = "App",
            FactoryClassFullName = "global::App.InfraFactories",
            Methods = ImmutableArray.Create(
                new FactoryMethodModel
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

        var source = new ServiceFactoryRegistrationTemplate(ImmutableArray.Create(model), "App")
            .RenderOutput().Text;

        source.Should().Contain("AddPragmaticServiceFactories");
        source.Should().Contain("services.AddSingleton<global::App.InfraFactories>();");
        source.Should().Contain(
            "services.AddSingleton<global::System.Data.IDbConnection>(sp => sp.GetRequiredService<global::App.InfraFactories>().CreateConnection(sp.GetRequiredService<global::Microsoft.Extensions.Configuration.IConfiguration>()));");
    }

    [Fact]
    public void RenderOutput_ScopedFactory_UsesAddScoped_AndOptionalParamUsesGetService()
    {
        var model = new ServiceFactoryModel
        {
            Namespace = "App",
            FactoryClassFullName = "global::App.F",
            Methods = ImmutableArray.Create(
                new FactoryMethodModel
                {
                    MethodName = "Make",
                    ReturnTypeFullName = "global::App.IThing",
                    Lifetime = "Scoped",
                    Parameters = ImmutableArray.Create(new FactoryParameterModel
                    {
                        FullTypeName = "global::App.IDep",
                        IsOptional = true
                    })
                }),
            Location = null
        };

        var source = new ServiceFactoryRegistrationTemplate(ImmutableArray.Create(model), "App")
            .RenderOutput().Text;

        source.Should().Contain(
            "services.AddScoped<global::App.IThing>(sp => sp.GetRequiredService<global::App.F>().Make(sp.GetService<global::App.IDep>()));");
    }

    [Fact]
    public void RenderOutput_NoFactories_EmitsEmptyMethod_SoTheEntryCanCallItUnconditionally()
    {
        var source = new ServiceFactoryRegistrationTemplate(ImmutableArray<ServiceFactoryModel>.Empty, "App")
            .RenderOutput().Text;

        source.Should().Contain("AddPragmaticServiceFactories");
        source.Should().Contain("return services;");
    }
}
