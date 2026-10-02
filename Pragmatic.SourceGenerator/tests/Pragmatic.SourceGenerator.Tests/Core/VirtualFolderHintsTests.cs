using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Tests.Core;

public sealed class VirtualFolderHintsTests
{
    [Fact]
    public void ForType_WithoutNamespace_ProducesFlatHint()
    {
        VirtualFolderHints.ForType("Invoice", "Repository").Should().Be("Invoice.Repository.g.cs");
    }

    [Fact]
    public void ForType_WithGlobalNamespaceSentinel_ProducesFlatHint()
    {
        VirtualFolderHints.ForType("Invoice", "Repository", "<global namespace>")
            .Should().Be("Invoice.Repository.g.cs");
    }

    [Fact]
    public void ForType_SameSimpleName_DifferentNamespaces_ProducesDistinctHints()
    {
        // Regression for the hint-name collision that would make AddSource throw and kill ALL codegen.
        var sales = VirtualFolderHints.ForType("Invoice", "Repository", "Sales");
        var archive = VirtualFolderHints.ForType("Invoice", "Repository", "Archive");

        sales.Should().Be("Sales.Invoice.Repository.g.cs");
        archive.Should().Be("Archive.Invoice.Repository.g.cs");
        sales.Should().NotBe(archive);
    }
}
