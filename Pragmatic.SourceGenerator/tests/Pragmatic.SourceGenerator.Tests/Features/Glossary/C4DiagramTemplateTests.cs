using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Glossary.Models;
using Pragmatic.SourceGenerator.Features.Glossary.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Glossary;

/// <summary>
///     The C4 generator emits PragmaticArchitecture.C4ContainerDiagram —
///     a Mermaid container diagram of the host's modules, distinguishing in-process [Include] from
///     remote [RemoteBoundary] — as a compile-time constant.
/// </summary>
public class C4DiagramTemplateTests
{
    [Fact]
    public void C4_RendersModules_MarkingRemoteOnes()
    {
        var modules = new[]
        {
            new C4ModuleModel { Name = "CatalogModule", IsRemote = false },
            new C4ModuleModel { Name = "BillingModule", IsRemote = true }
        }.ToEquatableArray();

        var source = new C4DiagramTemplate(modules, "MyApp").RenderOutput().Text;

        source.Should().Contain("namespace MyApp.Generated;");
        source.Should().Contain("public const string C4ContainerDiagram");
        source.Should().Contain("flowchart TD");
        source.Should().Contain("CatalogModule[CatalogModule]");
        source.Should().Contain("BillingModule[[BillingModule (remote)]]");
        source.Should().Contain("Host --> CatalogModule");
    }
}
