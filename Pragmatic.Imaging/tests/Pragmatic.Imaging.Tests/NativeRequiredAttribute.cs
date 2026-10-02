namespace Pragmatic.Imaging.Tests;

/// <summary>
/// Skip test if the native library is not available on this platform.
/// </summary>
public sealed class NativeRequiredFact : FactAttribute
{
    public NativeRequiredFact()
    {
        if (!TestHelper.IsNativeAvailable())
        {
            Skip = "Native library (pragmatic_native) not available on this platform.";
        }
    }
}
