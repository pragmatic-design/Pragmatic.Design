using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Pdf.Tests;

/// <summary>
/// What a caller reads when the native library cannot be loaded. It names every platform the package
/// carries a binary for, not only win-x64: the Linux binary is packed under the name the loader asks for.
/// </summary>
/// <remarks>
///     Built directly: on a machine where the native library loads, a render never reaches it.
/// </remarks>
public class TheUnavailableNativeMessageTests
{
    [Fact]
    public void NativeUnavailable_NamesEveryPlatformThatShips()
    {
        var message = NativePdfImports.NativeUnavailable(new DllNotFoundException()).Message;

        message.Should().Contain("win-x64").And.Contain("linux-x64");
        message.Should().NotContain("only for win-x64");
    }
}
