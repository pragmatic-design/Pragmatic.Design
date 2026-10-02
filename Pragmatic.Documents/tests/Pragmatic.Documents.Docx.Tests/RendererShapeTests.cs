using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>
/// All three renderers (Docx/Xlsx/Pdf) must share the same shape — stateless static utilities,
/// with no renderer interface nothing consumes.
/// </summary>
public class RendererShapeTests
{
    [Fact]
    public void DocxRenderer_IsStaticUtility_ConsistentWithXlsxAndPdf()
    {
        var docxType = typeof(DocxRenderer);

        // A static class compiles to abstract + sealed.
        docxType.IsAbstract.Should().BeTrue();
        docxType.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void IDocxRenderer_Interface_NoLongerExists()
    {
        // The renderer is static, so the assembly declares no IDocxRenderer.
        var interfaceType = typeof(DocxRenderer).Assembly.GetType("Pragmatic.Documents.Docx.IDocxRenderer");
        interfaceType.Should().BeNull();
    }
}
