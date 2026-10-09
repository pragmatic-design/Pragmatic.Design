namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>Subtraction: what formatting the dates costs.</summary>
internal readonly struct ConstantDatesProbe : IResponseProbe
{
    public static bool ConstantKeys => false;
    public static bool ConstantStrings => false;
    public static bool ConstantDates => true;
    public static bool RawNumbers => false;
    public static bool RawEncoderFree => false;
}
