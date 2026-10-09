namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>The writer as the generator emits it: every member through its own writer call.</summary>
internal readonly struct PlainProbe : IResponseProbe
{
    public static bool ConstantKeys => false;
    public static bool ConstantStrings => false;
    public static bool ConstantDates => false;
    public static bool RawNumbers => false;
    public static bool RawEncoderFree => false;
}
