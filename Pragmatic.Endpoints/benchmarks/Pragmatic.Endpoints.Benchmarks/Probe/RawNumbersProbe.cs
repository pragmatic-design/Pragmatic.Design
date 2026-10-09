namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>Candidate: runs of number members and arrays of numbers as one raw value each.</summary>
internal readonly struct RawNumbersProbe : IResponseProbe
{
    public static bool ConstantKeys => false;
    public static bool ConstantStrings => false;
    public static bool ConstantDates => false;
    public static bool RawNumbers => true;
    public static bool RawEncoderFree => false;
}
