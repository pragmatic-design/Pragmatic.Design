using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Pdf.Tests;

/// <summary>
///     Managed-side guards that do not require the native library: null validation and the
///     platform-capability probe. On a platform without the native backend, render calls surface a
///     clear <see cref="PdfRenderException"/> rather than a raw <see cref="DllNotFoundException"/>.
/// </summary>
public class PdfPlatformTests
{
    [Fact]
    public void Render_NullModel_ThrowsArgumentNullException()
    {
        // The null-guard runs before any native call, so this holds on every platform.
        Assert.Throws<ArgumentNullException>(() => PdfRenderer.Render(null!));
    }

    [Fact]
    public void IsSupported_IsCallableAndCached()
    {
        var first = PdfRenderer.IsSupported;
        // Second read returns the cached value — never throws, whatever the platform.
        PdfRenderer.IsSupported.Should().Be(first);
    }

    [Fact]
    public void Render_WhenNativeUnavailable_ThrowsPdfRenderExceptionNotDllNotFound()
    {
        if (PdfRenderer.IsSupported)
            return; // Native present on this platform — nothing to assert here.

        var model = new DocumentBuilder().Page(p => p.Text("hi")).Build();

        var act = () => PdfRenderer.Render(model);

        act.Should().Throw<PdfRenderException>()
            .WithMessage("*could not be loaded*");
    }
}
