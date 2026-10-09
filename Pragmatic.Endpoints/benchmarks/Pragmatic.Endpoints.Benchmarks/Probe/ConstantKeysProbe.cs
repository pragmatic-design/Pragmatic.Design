namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>Subtraction: what formatting a dictionary's integer keys costs.</summary>
internal readonly struct ConstantKeysProbe : IResponseProbe
{
    public static bool ConstantKeys => true;
    public static bool ConstantStrings => false;
    public static bool ConstantDates => false;
    public static bool RawNumbers => false;
    public static bool RawEncoderFree => false;
}
