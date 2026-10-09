namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>Candidate: everything the encoder cannot change as one raw value per run or subtree.</summary>
internal readonly struct RawEncoderFreeProbe : IResponseProbe
{
    public static bool ConstantKeys => false;
    public static bool ConstantStrings => false;
    public static bool ConstantDates => false;
    public static bool RawNumbers => true;
    public static bool RawEncoderFree => true;
}
