namespace Pragmatic.Imaging.Tests;

/// <summary>
/// Skip the theory if the native library is not available on this platform.
/// </summary>
public sealed class NativeRequiredTheory : TheoryAttribute
{
    public NativeRequiredTheory()
    {
        if (!TestHelper.IsNativeAvailable())
        {
            Skip = "Native library (pragmatic_native) not available on this platform.";
        }
    }
}
