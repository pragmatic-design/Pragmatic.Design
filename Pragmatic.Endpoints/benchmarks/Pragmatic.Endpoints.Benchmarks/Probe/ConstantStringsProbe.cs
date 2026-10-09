namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>Subtraction: what transcoding and escaping the string values costs.</summary>
internal readonly struct ConstantStringsProbe : IResponseProbe
{
    public static bool ConstantKeys => false;
    public static bool ConstantStrings => true;
    public static bool ConstantDates => false;
    public static bool RawNumbers => false;
    public static bool RawEncoderFree => false;
}
